#!/usr/bin/env bash
# ==============================================================================================
# container-build.sh - builds AND verifies libcodebrix_llama.so INSIDE the build container
# ==============================================================================================
#
# Do not run this on your workstation. build.sh runs it inside the derived image
# (codebrix-llama-build-<arch>) for the requested architecture, WITH THE NETWORK DISABLED:
#
#     podman run --network none ... codebrix-llama-build-<arch> bash /work/linux/container-build.sh
#
# That is not decoration. It is the mechanical proof of the rule this folder exists for: if any
# step ever tried to fetch a source file, a test asset or a dependency, it would fail here
# instead of quietly working on the machine that happened to have a network. Step 0 below prints
# the proof into the build log.
#
# It expects:
#   /work            llama-native-tools/, mounted read-write (only output/ is written)
#   $TARGET_RID      linux-x64 | linux-arm64 | linux-riscv64
#   the pins         passed through the environment by build.sh, which sources pins.env
#
# Everything it compiles is already in the repository: /work/llama.cpp is the vendored upstream
# subset, /work/wrapper is our CMake project, /work/test-vectors holds the conformance assets.
# There is nothing to download.
#
#   RUN 2026-09-15 for all three RIDs (see build.sh and BUILD-PROVENANCE.txt); unchanged by
#   the first-run fixes, which were in the wrapper and two Containerfiles.
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: container-build.sh failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

# first_line: the first line of stdin WITHOUT the `head -1` pitfall (SIGPIPE under pipefail).
first_line() { sed -n '1p'; }

WORK=/work
SRC="$WORK/${LLAMA_DIR:-llama.cpp}"
WRAPPER="$WORK/wrapper"
PATCHES="$WORK/patches"
SCRATCH=/tmp/codebrix-llama-src
BUILD=/tmp/codebrix-llama-build
OUT="$WORK/output/$TARGET_RID"
LIB_NAME="lib${LIBRARY_BASENAME}.so"
LICENSE_FILE_NAME="LICENSE-LlamaCpp.txt"
STARTED_AT="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
START_SECONDS="$SECONDS"

MODE="${MODE:-build}"                       # build | generate-expected
MODEL_FILE="$WORK/${TEST_VECTOR_MODEL}"
EXPECTED_FILE="$WORK/${TEST_VECTOR_EXPECTED}"

EXTRA_REQUIRED_SYMBOLS="ggml_backend_dev_count ggml_backend_dev_get ggml_backend_dev_name ggml_backend_dev_description ggml_backend_dev_type ggml_backend_dev_memory ggml_log_set gguf_init_from_file gguf_free gguf_get_n_kv gguf_get_key gguf_get_val_str gguf_get_n_tensors codebrix_llama_build_info codebrix_llama_rid"
ALLOWED_EXPORT_PREFIXES="llama_ ggml_ gguf_ codebrix_llama_"

echo "=============================================================================="
echo " codebrix_llama (llama.cpp $LLAMA_TAG, $LLAMA_COMMIT) - $TARGET_RID"
echo "=============================================================================="
echo " started   : $STARTED_AT"
echo " container : $(grep PRETTY_NAME /etc/os-release 2>/dev/null | first_line | cut -d= -f2- | tr -d '"')"
echo " image     : ${DERIVED_IMAGE:-unknown}"
echo " base      : ${BASE_IMAGE_REF:-unknown}"
echo " arch      : $(uname -m)"
echo " mode      : $MODE"
echo

# ----------------------------------------------------------------------------------------------
# 0. Prove there is no network.
# ----------------------------------------------------------------------------------------------
IFACES="$(ls /sys/class/net 2>/dev/null | tr '\n' ' ' | sed 's/ *$//')"
echo "--- network ---"
echo "  interfaces: ${IFACES:-none}"
if [ "$IFACES" = "lo" ] || [ -z "$IFACES" ]; then
    echo "  [ok] no external network interface - this build cannot reach outside the repository"
else
    echo "  [warn] this container HAS a network interface ($IFACES)."
    echo "         The build still fetches nothing, but the proof is weaker. build.sh normally"
    echo "         passes --network none; something overrode it."
fi
echo

# ----------------------------------------------------------------------------------------------
# 1. Toolchain. Installed by Containerfile.<arch> when the derived image was built.
# ----------------------------------------------------------------------------------------------
echo "--- toolchain ---"
for t in cc c++ cmake ninja file xz nm objdump readelf strip; do
    command -v "$t" > /dev/null 2>&1 || { echo "ERROR: $t is missing from this image. Rebuild it: FORCE_IMAGE_REBUILD=1 ./build.sh" >&2; exit 1; }
done
CC_VERSION="$(cc --version | first_line)"
CXX_VERSION="$(c++ --version | first_line)"
CMAKE_ACTUAL="$(cmake --version | first_line)"
NINJA_ACTUAL="$(ninja --version)"
GLIBC_ACTUAL="$(ldd --version | first_line)"
echo "  cc      : $CC_VERSION"
echo "  c++     : $CXX_VERSION"
echo "  cmake   : $CMAKE_ACTUAL (pinned $CMAKE_VERSION)"
echo "  ninja   : $NINJA_ACTUAL (pinned $NINJA_VERSION)"
echo "  glibc   : $GLIBC_ACTUAL"
echo

# ----------------------------------------------------------------------------------------------
# 2. Source. Copied to scratch and built THERE, so /work/llama.cpp is never written to.
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
[ -f "$SRC/CMakeLists.txt" ] || { echo "ERROR: no vendored llama.cpp source at $SRC" >&2; exit 1; }
[ -f "$MODEL_FILE" ] || { echo "ERROR: no conformance model at $MODEL_FILE" >&2; exit 1; }
MODEL_SHA="$(sha256sum "$MODEL_FILE" | cut -d' ' -f1)"
[ "$MODEL_SHA" = "$TEST_VECTOR_MODEL_SHA256" ] || {
    echo "ERROR: $MODEL_FILE has sha256 $MODEL_SHA but pins.env says $TEST_VECTOR_MODEL_SHA256." >&2
    echo "       The committed conformance model has been altered. Restore it from git." >&2; exit 1; }
rm -rf "$SCRATCH" "$BUILD"
cp -a "$SRC" "$SCRATCH"
echo "  copied $SRC -> $SCRATCH"

PATCHES_APPLIED="none"
if [ -d "$PATCHES" ] && ls "$PATCHES"/*.patch > /dev/null 2>&1; then
    PATCHES_APPLIED=""
    for p in "$PATCHES"/*.patch; do
        echo "  applying $(basename "$p")"
        ( cd "$SCRATCH" && patch -p1 --forward --batch < "$p" ) \
            || { echo "ERROR: patch $(basename "$p") did not apply. Fix it; patches are never applied best-effort." >&2; exit 1; }
        PATCHES_APPLIED="$PATCHES_APPLIED $(basename "$p")"
    done
    PATCHES_APPLIED="${PATCHES_APPLIED# }"
fi
echo "  patches applied: $PATCHES_APPLIED"
echo

# ----------------------------------------------------------------------------------------------
# 3. Configure + build the wrapper project. Options come from pins.env via the environment.
# ----------------------------------------------------------------------------------------------
BUILD_INFO="llama.cpp $LLAMA_TAG ($LLAMA_COMMIT) | ggml $GGML_VERSION | $TARGET_RID | built $STARTED_AT in ${DERIVED_IMAGE:-unknown} ($(grep PRETTY_NAME /etc/os-release 2>/dev/null | first_line | cut -d= -f2- | tr -d '"')) | $CC_VERSION"
echo "--- configuring ---"
echo "  cmake -S $WRAPPER -B $BUILD -G Ninja -DLLAMA_SOURCE_DIR=$SCRATCH -DCODEBRIX_LLAMA_RID=$TARGET_RID"
echo "        $LLAMA_CMAKE_OPTIONS"
echo "        $LLAMA_CMAKE_OPTIONS_ARCH"
mkdir -p "$BUILD"
# shellcheck disable=SC2086
cmake -S "$WRAPPER" -B "$BUILD" -G Ninja \
    -DLLAMA_SOURCE_DIR="$SCRATCH" \
    -DCODEBRIX_LLAMA_RID="$TARGET_RID" \
    -DCODEBRIX_LLAMA_BUILD_INFO="$BUILD_INFO" \
    $LLAMA_CMAKE_OPTIONS $LLAMA_CMAKE_OPTIONS_ARCH > "$BUILD/configure.log" 2>&1 \
    || { echo "ERROR: cmake configure failed:" >&2; tail -40 "$BUILD/configure.log" >&2; exit 1; }
grep -E 'codebrix_llama:|CMake Warning' "$BUILD/configure.log" | sed 's/^/  /' || true

echo
echo "--- building ($(nproc) jobs) ---"
cmake --build "$BUILD" -j "$(nproc)" > "$BUILD/build.log" 2>&1 \
    || { echo "ERROR: build failed:" >&2; grep -E -B2 -A8 'error:|FAILED:' "$BUILD/build.log" | head -80 >&2; exit 1; }
echo "  $(tail -1 "$BUILD/build.log")"
echo

# ----------------------------------------------------------------------------------------------
# 4. Collect. ONE unversioned file - LibraryImport("codebrix_llama") probes exactly this name.
# ----------------------------------------------------------------------------------------------
BUILT="$BUILD/$LIB_NAME"
[ -f "$BUILT" ] || { echo "ERROR: $BUILT was not produced." >&2; exit 1; }
for t in smoke-test generate-test-model conformance-check; do
    [ -x "$BUILD/$t" ] || { echo "ERROR: the gate tool $t was not built." >&2; exit 1; }
done

echo "--- collecting ---"
rm -rf "$OUT"
mkdir -p "$OUT/unstripped"
cp "$BUILT" "$OUT/unstripped/$LIB_NAME"
cp "$BUILT" "$OUT/$LIB_NAME"
chmod 0755 "$OUT/$LIB_NAME" "$OUT/unstripped/$LIB_NAME"
SIZE_UNSTRIPPED="$(stat -c %s "$OUT/unstripped/$LIB_NAME")"
strip --strip-unneeded "$OUT/$LIB_NAME"
SIZE_STRIPPED="$(stat -c %s "$OUT/$LIB_NAME")"
BUILD_ID="$(readelf -n "$OUT/$LIB_NAME" 2>/dev/null | awk '/Build ID/{print $3}' | first_line)"
echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped: $SIZE_STRIPPED bytes"
echo "  build-id  : ${BUILD_ID:-none}"
cp "$SRC/LICENSE" "$OUT/$LICENSE_FILE_NAME"
echo "  $LICENSE_FILE_NAME : llama.cpp's LICENSE (MIT) copied beside the binary"
echo

# ----------------------------------------------------------------------------------------------
# 5. THE GATE. A build that fails any check does not get staged, and this script exits non-zero.
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
FAILED=0
fail() { echo "  [FAIL] $1"; FAILED=1; }
pass() { echo "  [ok] $1"; }

LIB="$OUT/$LIB_NAME"

# 5a. Architecture.
FILE_OUT="$(file -b "$LIB")"
case "$TARGET_RID" in
    linux-x64)     WANT_ARCH="x86-64" ;;
    linux-arm64)   WANT_ARCH="ARM aarch64" ;;
    linux-riscv64) WANT_ARCH="UCB RISC-V" ;;
    *)             WANT_ARCH="" ;;
esac
case "$FILE_OUT" in
    *"$WANT_ARCH"*) pass "architecture: $FILE_OUT" ;;
    *)              fail "architecture mismatch: expected '$WANT_ARCH', file says: $FILE_OUT" ;;
esac

# 5b. Required exports: every LLAMA_API function in the header the library was built from + extras.
REQUIRED="$(grep -v -E '^\s*//' "$SCRATCH/include/llama.h" | tr '\n' ' ' \
    | grep -oE 'LLAMA_API[^;(]*\b(llama_[a-z0-9_]+) *\(' | grep -oE 'llama_[a-z0-9_]+ *\($' | tr -d ' (' | sort -u; \
    printf '%s\n' $EXTRA_REQUIRED_SYMBOLS)"
EXPORTS="$(nm -D --defined-only "$LIB" | awk '{print $3}')"
MISSING=""
COUNT=0
for sym in $REQUIRED; do
    COUNT=$((COUNT + 1))
    printf '%s\n' "$EXPORTS" | grep -x "$sym" > /dev/null || MISSING="$MISSING $sym"
done
if [ -n "$MISSING" ]; then
    fail "missing exports:$MISSING"
else
    pass "all $COUNT required symbols exported ($(printf '%s\n' "$REQUIRED" | grep -c '^llama_') from include/llama.h + extras)"
fi

# 5c. No foreign exports - the version script must have kept the surface to the four prefixes.
FOREIGN="$(printf '%s\n' "$EXPORTS" | grep -v -E "^($(printf '%s' "$ALLOWED_EXPORT_PREFIXES" | sed 's/ /|/g'))" || true)"
if [ -n "$FOREIGN" ]; then
    fail "exports outside the llama_/ggml_/gguf_/codebrix_llama_ surface: $(printf '%s\n' "$FOREIGN" | head -5 | tr '\n' ' ')..."
else
    pass "export surface is exactly llama_* / ggml_* / gguf_* / codebrix_llama_* ($(printf '%s\n' "$EXPORTS" | grep -c .) symbols)"
fi

# 5d. No dangling symbols, and only universal system libraries as dependencies. libstdc++ and
#     libgcc_s must NOT appear: the wrapper links them statically.
LDD_OUT="$(ldd -r "$LIB" 2>&1 || true)"
UNDEFINED="$(printf '%s\n' "$LDD_OUT" | grep -i 'undefined symbol' || true)"
if [ -n "$UNDEFINED" ]; then
    fail "ldd -r reports undefined symbols:"
    printf '%s\n' "$UNDEFINED" | sed 's/^/         /'
else
    pass "ldd -r: no undefined symbols"
fi

DEPS="$(objdump -p "$LIB" | awk '/NEEDED/ {print $2}' | sort)"
UNEXPECTED=""
for d in $DEPS; do
    case " $ALLOWED_DEPS " in
        *" $d "*) ;;
        *) UNEXPECTED="$UNEXPECTED $d" ;;
    esac
done
if [ -n "$UNEXPECTED" ]; then
    fail "unexpected dynamic dependencies:$UNEXPECTED (allowed: $ALLOWED_DEPS). libstdc++/libgcc_s here means -static-libstdc++ -static-libgcc did not take."
else
    pass "dependencies are system-only: $(printf '%s ' $DEPS)"
fi

# 5e. glibc floor - CHECKED, not merely reported.
GLIBC_FLOOR="$(objdump -T "$LIB" | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sed 's/GLIBC_//' | sort -V -u | tail -1)"
GLIBC_FLOOR="${GLIBC_FLOOR:-none}"
if [ "$GLIBC_FLOOR" = "none" ]; then
    pass "glibc floor: none referenced"
elif printf '%s\n%s\n' "$GLIBC_FLOOR" "$GLIBC_MAX" | sort -V -C; then
    pass "glibc floor: $GLIBC_FLOOR (allowed <= $GLIBC_MAX)"
else
    fail "glibc floor $GLIBC_FLOOR is NEWER than the allowed $GLIBC_MAX - this binary would not load on the systems the package promises"
fi

# The gate tools resolve lib${LIBRARY_BASENAME}.so through RPATH $ORIGIN - their own directory.
# Put the STAGED (stripped) file there so every check below exercises the shipped bytes.
cp "$LIB" "$BUILD/$LIB_NAME"

# 5f. dlopen smoke test.
echo "  --- dlopen smoke test ---"
if "$BUILD/smoke-test" "$BUILD/$LIB_NAME" "$TARGET_RID" | sed 's/^/    /'; then
    pass "smoke test"
else
    fail "smoke test"
fi

# 5g. The gguf writer in THIS build reproduces the committed model byte for byte.
echo "  --- model regeneration ---"
REGEN="$BUILD/regenerated-model.gguf"
if "$BUILD/generate-test-model" "$REGEN" | sed 's/^/    /' && [ -f "$REGEN" ]; then
    REGEN_SHA="$(sha256sum "$REGEN" | cut -d' ' -f1)"
    if [ "$REGEN_SHA" = "$TEST_VECTOR_MODEL_SHA256" ]; then
        pass "regenerated model is byte-identical to the committed test-vectors model"
    else
        fail "regenerated model sha256 $REGEN_SHA differs from the committed $TEST_VECTOR_MODEL_SHA256"
    fi
else
    fail "generate-test-model failed"
fi

# 5h. Conformance.
echo "  --- conformance ($TARGET_RID, CPU) ---"
CONFORMANCE_SUMMARY=""
if [ "$MODE" = "generate-expected" ]; then
    GEN="$WORK/output/generated-expected.txt"
    "$BUILD/conformance-check" "$MODEL_FILE" --generate --gpu-layers 0 > "$GEN" 2>/dev/null
    pass "logits written to output/generated-expected.txt (MODE=generate-expected: nothing was compared)"
    CONFORMANCE_SUMMARY="
  generate-expected mode: nothing compared"
else
    if CONF_OUT="$("$BUILD/conformance-check" "$MODEL_FILE" --check "$EXPECTED_FILE" --tolerance "$CONFORMANCE_TOLERANCE" --gpu-layers 0 2>/dev/null)"; then
        printf '%s\n' "$CONF_OUT" | sed 's/^/    /'
        pass "conformance on the CPU path"
    else
        printf '%s\n' "$CONF_OUT" | sed 's/^/    /'
        fail "conformance on the CPU path"
    fi
    CONFORMANCE_SUMMARY="
  CPU  : $(printf '%s\n' "$CONF_OUT" | grep 'conformance:' || echo 'no summary line')"
fi

if [ "$FAILED" -ne 0 ]; then
    echo
    echo "VERIFICATION FAILED for $TARGET_RID. output/$TARGET_RID is left in place for inspection,"
    echo "but nothing was staged and this build must not be adopted into the package."
    rm -rf "$WORK/output/staging/$TARGET_RID"
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 6. Stage + record.
# ----------------------------------------------------------------------------------------------
SHA="$(sha256sum "$LIB" | cut -d' ' -f1)"
SHA_UNSTRIPPED="$(sha256sum "$OUT/unstripped/$LIB_NAME" | cut -d' ' -f1)"
mkdir -p "$WORK/output/staging/$TARGET_RID"
xz -9e -k -c "$LIB" > "$WORK/output/staging/$TARGET_RID/$LIB_NAME.xz"
SIZE_XZ="$(stat -c %s "$WORK/output/staging/$TARGET_RID/$LIB_NAME.xz")"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
codebrix_llama native library - build information
==============================================================================
RID              : $TARGET_RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s (wall clock inside the container)
Built by         : llama-native-tools/linux/build.sh -> container-build.sh
Network          : DISABLED during this build (interfaces: ${IFACES:-none})

Build machine
------------------------------------------------------------------------------
Derived image    : ${DERIVED_IMAGE:-unknown}
Base image       : ${BASE_IMAGE_REF:-unknown}
Container OS     : $(grep PRETTY_NAME /etc/os-release 2>/dev/null | first_line | cut -d= -f2- | tr -d '"')
Machine          : $(uname -m)
Compiler         : $CC_VERSION / $CXX_VERSION
cmake            : $CMAKE_ACTUAL (pinned $CMAKE_VERSION)
ninja            : $NINJA_ACTUAL (pinned $NINJA_VERSION)
Container glibc  : $GLIBC_ACTUAL

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
llama.cpp        : tag $LLAMA_TAG, commit $LLAMA_COMMIT, ggml $GGML_VERSION
Vendored at      : llama-native-tools/llama.cpp/ (see UPSTREAM.txt)
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
cmake options    : $LLAMA_CMAKE_OPTIONS
arch options     : ${LLAMA_CMAKE_OPTIONS_ARCH:-(none)}
GPU backend      : none (CPU-only slice)
Wrapper          : llama-native-tools/wrapper/CMakeLists.txt (static archives, whole-archive, one .so,
                   libstdc++/libgcc linked statically, version-script export list)

Result
------------------------------------------------------------------------------
File             : $LIB_NAME  (unversioned on purpose - LibraryImport("$LIBRARY_BASENAME") probes this name)
Build-id         : ${BUILD_ID:-none}
Size (stripped)  : $SIZE_STRIPPED bytes
Size (unstripped): $SIZE_UNSTRIPPED bytes  -> unstripped/$LIB_NAME
Size (xz staged) : $SIZE_XZ bytes          -> ../staging/$TARGET_RID/$LIB_NAME.xz
SHA256           : $SHA
SHA256 unstripped: $SHA_UNSTRIPPED
glibc floor      : ${GLIBC_FLOOR} (allowed <= ${GLIBC_MAX:-n/a})
Dynamic deps     : $(printf '%s ' $DEPS)
Licence beside it: $LICENSE_FILE_NAME (a verbatim copy of llama.cpp's LICENSE, MIT)

Conformance (test-vectors/codebrix-conformance-tiny.gguf vs EXPECTED.txt, tolerance $CONFORMANCE_TOLERANCE)
------------------------------------------------------------------------------$CONFORMANCE_SUMMARY
EOF

echo "--- done ---"
echo "  $OUT/$LIB_NAME"
echo "  sha256 $SHA"
echo "  staged $WORK/output/staging/$TARGET_RID/$LIB_NAME.xz ($SIZE_XZ bytes)"
echo "  ${ELAPSED}s"
