#!/usr/bin/env bash
# ==============================================================================================
# build-common.sh - shared machinery for the macOS codebrix_llama builds
# ==============================================================================================
#
# Sourced by build-osx-x64.sh and build-osx-arm64.sh; not meant to be run directly.
#
# Everything it needs is in this repository: ../llama.cpp (the vendored source subset),
# ../wrapper (our CMake project that folds it into one shared library), ../smoke-test.c,
# ../test-vectors/ (the conformance model, its reference and the two C tools), and
# ../linux/pins.env (the pins, shared by all three platforms - see README.txt for why they live
# in the linux folder). Nothing is downloaded.
#
# STATUS: build-osx-x64.sh was run for real on the Intel Mac mini on 2026-09-15 (native route).
#         build-osx-arm64.sh, and the CROSS route of build-osx-x64.sh, have NOT been run yet -
#         see README.txt, "WHAT HAS AND HAS NOT BEEN VERIFIED".
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: the macOS codebrix_llama build failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

# The first line of stdin, without the `head -1` pitfall: `cmd | head -1` inside $(...) under
# `set -o pipefail` can report 141 because head exits first and the writer takes SIGPIPE.
# `sed -n 1p` reads its input to the end. (Learned the hard way in dav1d-native-tools.)
first_line() { sed -n '1p'; }

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"     # llama-native-tools/
PINS_FILE="$TOOLS_DIR/linux/pins.env"

[ -f "$PINS_FILE" ] || {
    echo "ERROR: $PINS_FILE is missing. It is part of the repository; if it vanished after a" >&2
    echo "       clone, check the root .gitignore's blanket '*.env' rule (see README.txt)." >&2
    exit 1; }
# shellcheck source=../linux/pins.env
. "$PINS_FILE"

SRC_DIR="$TOOLS_DIR/$LLAMA_DIR"
WRAPPER_DIR="$TOOLS_DIR/wrapper"
PATCH_DIR="$TOOLS_DIR/patches"
MODEL_FILE="$TOOLS_DIR/$TEST_VECTOR_MODEL"
EXPECTED_FILE="$TOOLS_DIR/$TEST_VECTOR_EXPECTED"
LIB_NAME="lib${LIBRARY_BASENAME}.dylib"
LICENSE_FILE_NAME="LICENSE-LlamaCpp.txt"

# ----------------------------------------------------------------------------------------------
# Dynamic dependencies a correct build may have. Anything else means the package would demand a
# library be installed on the user's Mac. libc++ is Apple's system C++ runtime (it ships with
# the OS, unlike libstdc++ on Linux), so it is allowed.
# ----------------------------------------------------------------------------------------------
ALLOWED_DYLIBS_CPU="/usr/lib/libSystem.B.dylib /usr/lib/libc++.1.dylib /System/Library/Frameworks/Accelerate.framework/Versions/A/Accelerate"
ALLOWED_DYLIBS_METAL="$ALLOWED_DYLIBS_CPU /usr/lib/libobjc.A.dylib /System/Library/Frameworks/Foundation.framework/Versions/C/Foundation /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation /System/Library/Frameworks/Metal.framework/Versions/A/Metal /System/Library/Frameworks/MetalKit.framework/Versions/A/MetalKit"

# Entry points the gate requires BEYOND every LLAMA_API function in include/llama.h (which is
# extracted from the header at gate time, so it cannot drift): the ggml device enumeration and
# gguf metadata functions the managed binding uses, and the two CodeBrix-authored functions.
EXTRA_REQUIRED_SYMBOLS="ggml_backend_dev_count ggml_backend_dev_get ggml_backend_dev_name ggml_backend_dev_description ggml_backend_dev_type ggml_backend_dev_memory ggml_log_set gguf_init_from_file gguf_free gguf_get_n_kv gguf_get_key gguf_get_val_str gguf_get_n_tensors codebrix_llama_build_info codebrix_llama_rid"

# Every exported symbol must start with one of these (after Mach-O's leading underscore).
# The export list in ../wrapper/exports-macos.txt enforces it at link time; the gate re-checks
# the finished file so a change to the list cannot silently widen the surface.
ALLOWED_EXPORT_PREFIXES="_llama_ _ggml_ _gguf_ _codebrix_llama_"

GATE_FAILED=0
gate_pass() { echo "  [ok] $1"; }
gate_fail() { echo "  [FAIL] $1"; GATE_FAILED=1; }

# ----------------------------------------------------------------------------------------------
require_tool() {
    local tool="$1" hint="$2"
    command -v "$tool" > /dev/null 2>&1 || {
        echo "ERROR: $tool is not on PATH." >&2
        echo >&2
        echo "$hint" >&2
        echo >&2
        echo "This script installs nothing. See README.txt, PREREQUISITES." >&2
        exit 1; }
}

check_common_prerequisites() {
    require_tool cc      'Install the Xcode Command Line Tools:   xcode-select --install'
    require_tool cmake   "Install it with Homebrew:   brew install cmake   (pinned version in pins.env: $CMAKE_VERSION)"
    require_tool ninja   "Install it with Homebrew:   brew install ninja   (pinned version in pins.env: $NINJA_VERSION)"
    for t in otool install_name_tool codesign nm file dsymutil strip shasum; do
        require_tool "$t" 'Part of the Xcode Command Line Tools:   xcode-select --install'
    done

    [ -f "$SRC_DIR/CMakeLists.txt" ] && [ -f "$SRC_DIR/ggml/CMakeLists.txt" ] || {
        echo "ERROR: the vendored llama.cpp source is missing from $SRC_DIR." >&2
        echo "       Nothing can be built without it, and it is not downloaded - it is part of this" >&2
        echo "       repository. Restore it from git." >&2
        exit 1; }
    [ -f "$WRAPPER_DIR/CMakeLists.txt" ] || { echo "ERROR: $WRAPPER_DIR/CMakeLists.txt is missing; it is part of the repository." >&2; exit 1; }
    [ -f "$MODEL_FILE" ] || {
        echo "ERROR: the conformance model $MODEL_FILE is missing. The conformance gate cannot run" >&2
        echo "       without it, and a build that skips conformance is not a build worth shipping." >&2
        exit 1; }
    [ -f "$EXPECTED_FILE" ] || {
        echo "ERROR: the expected-logits file $EXPECTED_FILE is missing. See test-vectors/README.txt." >&2
        exit 1; }

    # The committed model must be intact before anything is compared against it.
    local model_sha
    model_sha="$(shasum -a 256 "$MODEL_FILE" | cut -d' ' -f1)"
    [ "$model_sha" = "$TEST_VECTOR_MODEL_SHA256" ] || {
        echo "ERROR: $MODEL_FILE has sha256 $model_sha but pins.env says $TEST_VECTOR_MODEL_SHA256." >&2
        echo "       The committed conformance model has been altered or corrupted. Restore it from git," >&2
        echo "       or - if the change was deliberate - update pins.env AND re-establish EXPECTED.txt." >&2
        exit 1; }
}

# ----------------------------------------------------------------------------------------------
# The vendored tree is copied to scratch and built there, so ../llama.cpp is never written to
# and stays a verifiable, unmodified upstream snapshot. Patches (none today) apply to the copy.
# ----------------------------------------------------------------------------------------------
copy_source_to_scratch() {
    local scratch="$1"
    rm -rf "$scratch"
    cp -a "$SRC_DIR" "$scratch"
    echo "  copied $SRC_DIR -> $scratch"

    PATCHES_APPLIED="none"
    if [ -d "$PATCH_DIR" ] && ls "$PATCH_DIR"/*.patch > /dev/null 2>&1; then
        PATCHES_APPLIED=""
        for p in "$PATCH_DIR"/*.patch; do
            echo "  applying $(basename "$p")"
            ( cd "$scratch" && patch -p1 --forward --batch < "$p" ) \
                || { echo "ERROR: patch $(basename "$p") did not apply. Fix it; patches are never applied best-effort." >&2; exit 1; }
            PATCHES_APPLIED="$PATCHES_APPLIED $(basename "$p")"
        done
        PATCHES_APPLIED="${PATCHES_APPLIED# }"
    fi
    echo "  patches applied: $PATCHES_APPLIED"
}

# ----------------------------------------------------------------------------------------------
# Configure + build the wrapper project.
#   $1 scratch source   $2 build dir   $3 RID   $4 arch-specific cmake options (from pins.env)
#   $5 extra cmake options (e.g. -DCMAKE_OSX_ARCHITECTURES=x86_64 for a cross build), may be ""
# Every option comes from pins.env so a build log and the pins can never disagree; the wrapper
# additionally FORCES the settings the gate depends on.
# ----------------------------------------------------------------------------------------------
build_library() {
    local scratch="$1" build="$2" rid="$3" arch_opts="$4" extra_opts="$5"
    local build_info="llama.cpp $LLAMA_TAG ($LLAMA_COMMIT) | ggml $GGML_VERSION | $rid | built $STARTED_AT on macOS $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m)) | $(cc --version | first_line)"
    rm -rf "$build"
    mkdir -p "$build"
    export MACOSX_DEPLOYMENT_TARGET="$MACOS_MIN_VERSION"

    echo "  cmake -S $WRAPPER_DIR -B $build -G Ninja"
    echo "        -DLLAMA_SOURCE_DIR=$scratch -DCODEBRIX_LLAMA_RID=$rid"
    echo "        -DCMAKE_OSX_DEPLOYMENT_TARGET=$MACOS_MIN_VERSION $extra_opts"
    echo "        $LLAMA_CMAKE_OPTIONS"
    echo "        $arch_opts"
    # shellcheck disable=SC2086
    cmake -S "$WRAPPER_DIR" -B "$build" -G Ninja \
        -DLLAMA_SOURCE_DIR="$scratch" \
        -DCODEBRIX_LLAMA_RID="$rid" \
        -DCODEBRIX_LLAMA_BUILD_INFO="$build_info" \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$MACOS_MIN_VERSION" \
        $extra_opts $LLAMA_CMAKE_OPTIONS $arch_opts > "$build/configure.log" 2>&1 \
        || { echo "ERROR: cmake configure failed - see $build/configure.log" >&2; tail -30 "$build/configure.log" >&2; exit 1; }
    grep -E 'codebrix_llama:|CMake Warning' "$build/configure.log" | sed 's/^/  /' || true

    echo "  building (log: $build/build.log) ..."
    cmake --build "$build" -j "$(sysctl -n hw.ncpu)" > "$build/build.log" 2>&1 \
        || { echo "ERROR: build failed - see $build/build.log" >&2; grep -E -B2 -A8 'error:|FAILED:' "$build/build.log" | head -60 >&2; exit 1; }
    echo "  built: $(tail -1 "$build/build.log")"
}

# ----------------------------------------------------------------------------------------------
# The complete public API, read from the header the library was built from. Comment lines are
# dropped first (upstream keeps a commented-out "TODO" declaration in llama.h), and the header
# is joined into one line so a declaration split across lines is still seen.
# ----------------------------------------------------------------------------------------------
required_symbols_from_header() {
    local header="$1"
    grep -v -E '^\s*//' "$header" \
        | tr '\n' ' ' \
        | grep -oE 'LLAMA_API[^;(]*\b(llama_[a-z0-9_]+) *\(' \
        | grep -oE 'llama_[a-z0-9_]+ *\($' \
        | tr -d ' (' \
        | sort -u
}

# ----------------------------------------------------------------------------------------------
# THE GATE.
#   $1 the staged dylib (stripped, install-named, signed - the file the package ships)
#   $2 expected arch (arm64 | x86_64)
#   $3 the build dir (for the header the API list comes from and the three gate tools)
#   $4 can this machine run $2 code (yes | no)
#   $5 does this build include Metal (yes | no) - selects the allowed-dependency list and
#      whether the GPU conformance pass is REQUIRED
# ----------------------------------------------------------------------------------------------
verify_dylib() {
    local dylib="$1" want_arch="$2" build="$3" can_run="$4" has_metal="$5"
    local scratch_header="$build/llama-source-header.h"

    # 1. Architecture ---------------------------------------------------------------------------
    local file_out
    file_out="$(file -b "$dylib")"
    case "$file_out" in
        *"$want_arch"*) gate_pass "architecture: $file_out" ;;
        *)              gate_fail "architecture mismatch: expected $want_arch, file says: $file_out" ;;
    esac

    # 2. Required exports: every LLAMA_API function in the header + the extras --------------------
    local exports missing="" required count
    exports="$(nm -gU "$dylib" | awk '{print $NF}')"
    required="$(required_symbols_from_header "$scratch_header"; printf '%s\n' $EXTRA_REQUIRED_SYMBOLS)"
    count=0
    for sym in $required; do
        count=$((count + 1))
        printf '%s\n' "$exports" | grep -x "_$sym" > /dev/null || missing="$missing $sym"
    done
    if [ -n "$missing" ]; then
        gate_fail "missing exports:$missing"
    else
        gate_pass "all $count required symbols exported ($(printf '%s\n' "$required" | grep -c '^llama_') from include/llama.h + extras)"
    fi

    # 3. No foreign exports - the surface is exactly the four prefixes ----------------------------
    local foreign
    foreign="$(printf '%s\n' "$exports" | grep -v -E "^($(printf '%s' "$ALLOWED_EXPORT_PREFIXES" | sed 's/ /|/g'))" || true)"
    if [ -n "$foreign" ]; then
        gate_fail "exports outside the llama_/ggml_/gguf_/codebrix_llama_ surface: $(printf '%s\n' "$foreign" | head -5 | tr '\n' ' ')..."
    else
        gate_pass "export surface is exactly llama_* / ggml_* / gguf_* / codebrix_llama_* ($(printf '%s\n' "$exports" | grep -c .) symbols)"
    fi

    # 4. Install name -----------------------------------------------------------------------------
    local install_name
    install_name="$(otool -D "$dylib" | sed -n '2p')"
    if [ "$install_name" = "@rpath/$LIB_NAME" ]; then
        gate_pass "install name: $install_name"
    else
        gate_fail "install name is '$install_name', expected @rpath/$LIB_NAME"
    fi

    # 5. Dependencies -----------------------------------------------------------------------------
    local allowed deps unexpected=""
    if [ "$has_metal" = "yes" ]; then allowed="$ALLOWED_DYLIBS_METAL"; else allowed="$ALLOWED_DYLIBS_CPU"; fi
    deps="$(otool -L "$dylib" | tail -n +2 | awk '{print $1}' | grep -v "^@rpath/$LIB_NAME\$" || true)"
    for d in $deps; do
        case " $allowed " in
            *" $d "*) ;;
            *) unexpected="$unexpected $d" ;;
        esac
    done
    if [ -n "$unexpected" ]; then
        gate_fail "unexpected dynamic dependencies:$unexpected"
    else
        gate_pass "dependencies are system-only: $(printf '%s ' $deps)"
    fi

    # 6. Deployment target - CHECKED, not merely reported -------------------------------------------
    local minos
    minos="$(otool -l "$dylib" | awk '/LC_BUILD_VERSION/{f=1} f && /minos/{print $2; exit}')"
    if [ -z "$minos" ]; then
        minos="$(otool -l "$dylib" | awk '/LC_VERSION_MIN_MACOSX/{f=1} f && /version/{print $2; exit}')"
    fi
    if [ "$minos" = "$MACOS_MIN_VERSION" ]; then
        gate_pass "minimum macOS: $minos"
    else
        gate_fail "minimum macOS is '$minos', expected $MACOS_MIN_VERSION - a build on a newer Mac must not silently raise the floor"
    fi

    # 7. Code signature ---------------------------------------------------------------------------
    if codesign -dv "$dylib" > /dev/null 2>&1; then
        gate_pass "code signature present ($(codesign -dv "$dylib" 2>&1 | grep -i '^Signature' | first_line || echo 'ad-hoc'))"
    else
        gate_fail "no code signature - Apple Silicon refuses to load an unsigned dylib"
    fi

    if [ "$can_run" != "yes" ]; then
        gate_fail "smoke test NOT RUN - this machine cannot execute $want_arch code (Rosetta 2 missing?). Reported as a failure on purpose: an unrun check is not a passed check."
        gate_fail "model regeneration NOT RUN - same reason"
        gate_fail "conformance NOT RUN - same reason"
        CONFORMANCE_SUMMARY="  not run: this machine cannot execute $want_arch code"
        return
    fi

    # The three gate tools were linked against the build-tree dylib and resolve
    # @rpath/$LIB_NAME through @loader_path, i.e. from their own directory. Put the STAGED file -
    # the exact bytes the package ships - there, so every check below exercises the shipped
    # library and not the pre-strip, pre-sign build product. (dav1d's Windows gate was once
    # fooled by a different dav1d.dll on PATH; this is the same lesson applied in advance.)
    cp "$dylib" "$build/$LIB_NAME"

    # 8. dlopen smoke test ------------------------------------------------------------------------
    echo "  --- dlopen smoke test ---"
    if "$build/smoke-test" "$build/$LIB_NAME" "$RID" | sed 's/^/    /'; then
        gate_pass "smoke test"
    else
        gate_fail "smoke test"
    fi

    # 9. The gguf writer in THIS build reproduces the committed conformance model byte for byte ----
    echo "  --- model regeneration ---"
    local regen="$build/regenerated-model.gguf" regen_sha
    if "$build/generate-test-model" "$regen" | sed 's/^/    /' && [ -f "$regen" ]; then
        regen_sha="$(shasum -a 256 "$regen" | cut -d' ' -f1)"
        if [ "$regen_sha" = "$TEST_VECTOR_MODEL_SHA256" ]; then
            gate_pass "regenerated model is byte-identical to the committed test-vectors model ($regen_sha)"
        else
            gate_fail "regenerated model sha256 $regen_sha differs from the committed $TEST_VECTOR_MODEL_SHA256 - the gguf writer or the generator's arithmetic differs on this platform"
        fi
    else
        gate_fail "generate-test-model failed"
    fi

    # 10. Conformance, CPU path -------------------------------------------------------------------
    echo "  --- conformance (CPU) ---"
    CONFORMANCE_SUMMARY=""
    local out
    if out="$("$build/conformance-check" "$MODEL_FILE" --check "$EXPECTED_FILE" --tolerance "$CONFORMANCE_TOLERANCE" --gpu-layers 0 2>/dev/null)"; then
        printf '%s\n' "$out" | sed 's/^/    /'
        gate_pass "conformance on the CPU path"
    else
        printf '%s\n' "$out" | sed 's/^/    /'
        gate_fail "conformance on the CPU path"
    fi
    CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  CPU  : $(printf '%s\n' "$out" | grep 'conformance:' || echo 'no summary line')"

    # 11. Conformance, GPU path - REQUIRED when the build carries Metal -------------------------------
    if [ "$has_metal" = "yes" ]; then
        echo "  --- conformance (Metal, all layers offloaded) ---"
        if out="$("$build/conformance-check" "$MODEL_FILE" --check "$EXPECTED_FILE" --tolerance "$CONFORMANCE_TOLERANCE" --gpu-layers 99 2>/dev/null)"; then
            printf '%s\n' "$out" | sed 's/^/    /'
            gate_pass "conformance on the Metal path"
        else
            printf '%s\n' "$out" | sed 's/^/    /'
            gate_fail "conformance on the Metal path (NaN logits here means the GPU is not supported by ggml-metal - see README.txt)"
        fi
        CONFORMANCE_SUMMARY="$CONFORMANCE_SUMMARY
  Metal: $(printf '%s\n' "$out" | grep 'conformance:' || echo 'no summary line')"
    fi
}

# ----------------------------------------------------------------------------------------------
# Collect the built dylib: unstripped copy, dSYM, strip, install name, sign, licence file.
#   $1 build dir   $2 output dir
# Order matters: strip, then install_name_tool, then codesign - the first two invalidate a
# signature, so signing has to be last (and the linker's own ad-hoc signature, applied to arm64
# output but NOT to x86_64, is gone by then either way).
# ----------------------------------------------------------------------------------------------
collect_dylib() {
    local build="$1" out="$2"
    local built="$build/$LIB_NAME"
    [ -f "$built" ] || { echo "ERROR: $built was not produced." >&2; exit 1; }
    for t in smoke-test generate-test-model conformance-check; do
        [ -x "$build/$t" ] || { echo "ERROR: the gate tool $t was not built." >&2; exit 1; }
    done
    # Keep the header the library was built from beside the build, for the export check.
    cp "$SCRATCH/include/llama.h" "$build/llama-source-header.h"

    rm -rf "$out"
    mkdir -p "$out/unstripped"
    cp "$built" "$out/unstripped/$LIB_NAME"
    cp "$built" "$out/$LIB_NAME"
    chmod 0755 "$out/$LIB_NAME" "$out/unstripped/$LIB_NAME"
    SIZE_UNSTRIPPED="$(stat -f %z "$out/unstripped/$LIB_NAME")"

    dsymutil "$out/unstripped/$LIB_NAME" -o "$out/$LIB_NAME.dSYM" || \
        echo "  [warn] dsymutil failed; crash reports from this build will be harder to read."

    strip -x "$out/$LIB_NAME"
    SIZE_STRIPPED="$(stat -f %z "$out/$LIB_NAME")"
    install_name_tool -id "@rpath/$LIB_NAME" "$out/$LIB_NAME"
    codesign --force --sign - "$out/$LIB_NAME"

    cp "$SRC_DIR/LICENSE" "$out/$LICENSE_FILE_NAME"
    echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped: $SIZE_STRIPPED bytes"
    echo "  $LICENSE_FILE_NAME : llama.cpp's LICENSE (MIT) copied beside the binary"
}
