#!/usr/bin/env bash
# ==============================================================================================
# build-osx-x64.sh - build libcodebrix_llama.dylib for the osx-x64 runtime identifier
# ==============================================================================================
#
#   RUN AND VERIFIED 2026-09-15 on an Intel Mac mini (macOS 15.8, Apple clang 17, cmake 4.4.3,
#   ninja 1.13.2) - the NATIVE route. See README.txt, "WHAT HAS AND HAS NOT BEEN VERIFIED".
#   The CROSS route (building this slice on an Apple Silicon Mac) has NOT been run yet.
#
# USAGE
#     cd llama-native-tools/macos
#     ./build-osx-x64.sh
#
# TWO ROUTES, chosen automatically from the host:
#   * On an Intel Mac (uname -m = x86_64): a native build. The whole gate runs on the machine.
#   * On an Apple Silicon Mac (arm64): a cross build via -DCMAKE_OSX_ARCHITECTURES=x86_64. Apple's
#     clang is a cross compiler by nature. ROSETTA 2 IS NEEDED TO VERIFY, not to build: the smoke
#     test, the model regeneration and the conformance decode have to RUN x86_64 code. Without
#     it those checks are reported as FAILURES rather than skipped quietly:
#         softwareupdate --install-rosetta
#
# THIS SLICE IS CPU-ONLY (Jeremy, 2026-09-15). Metal is OFF - pins.env LLAMA_CMAKE_OPTIONS_OSX_X64.
# That is a correctness decision, not only a size one: on 2026-09-15 the Metal backend at this
# llama.cpp commit returned NaN for every logit on an Intel UHD 630, so an Intel Mac cannot use
# it anyway. The dependency list of this slice is therefore libSystem, libc++ and Accelerate only.
#
# The two macOS slices stay TWO SEPARATE DYLIBS in two separate RID folders - deliberately not a
# universal binary. runtimes/osx-x64/native/ and runtimes/osx-arm64/native/ each want their own
# file; a fat binary would put both slices in both places and double the size of each for nothing.
#
# Output: ../output/osx-x64/
# It installs nothing: anything missing is named with the command that installs it.
# ==============================================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=build-common.sh
. "$SCRIPT_DIR/build-common.sh"

RID=osx-x64
ARCH=x86_64
OUT="$TOOLS_DIR/output/$RID"
SCRATCH=/tmp/codebrix-llama-src-$RID
BUILD=/tmp/codebrix-llama-build-$RID
STARTED_AT="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
START_SECONDS="$SECONDS"

echo "=============================================================================="
echo " codebrix_llama (llama.cpp $LLAMA_TAG, $LLAMA_COMMIT) - $RID"
echo "=============================================================================="
echo " started : $STARTED_AT"
echo " host    : $(uname -m), macOS $(sw_vers -productVersion 2>/dev/null || echo unknown)"
echo " source  : $SRC_DIR  (vendored - nothing is downloaded)"
echo

# ----------------------------------------------------------------------------------------------
# 1. Prerequisites and the route
# ----------------------------------------------------------------------------------------------
echo "--- prerequisites ---"
check_common_prerequisites
CC_VERSION="$(cc --version | first_line)"
echo "  cc       : $CC_VERSION"
echo "  cmake    : $(cmake --version | first_line) (pinned $CMAKE_VERSION)"
echo "  ninja    : $(ninja --version) (pinned $NINJA_VERSION)"
echo "  min macOS: $MACOS_MIN_VERSION"

HOST_ARCH="$(uname -m)"
CAN_RUN=no
EXTRA_CMAKE=""
if [ "$HOST_ARCH" = "x86_64" ]; then
    ROUTE="native (Intel Mac)"
    CAN_RUN=yes
else
    ROUTE="cross-compiled on $HOST_ARCH via CMAKE_OSX_ARCHITECTURES=x86_64"
    EXTRA_CMAKE="-DCMAKE_OSX_ARCHITECTURES=x86_64"
    if /usr/bin/pgrep -q oahd 2>/dev/null || [ -f /Library/Apple/usr/libexec/oah/libRosettaRuntime ]; then
        CAN_RUN=yes
    fi
fi
echo "  route    : $ROUTE"
if [ "$CAN_RUN" = "yes" ]; then
    echo "  execute  : x86_64 code can run on this machine - the full gate will be applied"
else
    echo "  execute  : NOT AVAILABLE. The build will run, but the smoke test, model regeneration and"
    echo "             conformance cannot, and will be reported as FAILURES. Install Rosetta 2 with"
    echo "             'softwareupdate --install-rosetta' and re-run, or verify this binary on an"
    echo "             Intel Mac before shipping it."
fi
echo

# ----------------------------------------------------------------------------------------------
# 2. Source + build
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
copy_source_to_scratch "$SCRATCH"
echo

echo "--- building ---"
build_library "$SCRATCH" "$BUILD" "$RID" "$LLAMA_CMAKE_OPTIONS_OSX_X64" "$EXTRA_CMAKE"
echo

# ----------------------------------------------------------------------------------------------
# 3. Collect
# ----------------------------------------------------------------------------------------------
echo "--- collecting ---"
collect_dylib "$BUILD" "$OUT"
echo

# ----------------------------------------------------------------------------------------------
# 4. The gate
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
verify_dylib "$OUT/$LIB_NAME" "$ARCH" "$BUILD" "$CAN_RUN" no

if [ "$GATE_FAILED" -ne 0 ]; then
    echo
    if [ "$CAN_RUN" != "yes" ]; then
        echo "Rosetta 2 is not installed, so the checks that have to RUN x86_64 code could not run."
        echo "They are counted as failures on purpose: an unrun check is not a passed check."
    fi
    echo "VERIFICATION INCOMPLETE OR FAILED for $RID - see above. $OUT is left for inspection,"
    echo "but this build must not be adopted into the package."
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 5. Stage + record
# ----------------------------------------------------------------------------------------------
SHA="$(shasum -a 256 "$OUT/$LIB_NAME" | cut -d' ' -f1)"
SHA_UNSTRIPPED="$(shasum -a 256 "$OUT/unstripped/$LIB_NAME" | cut -d' ' -f1)"
UUID="$(dwarfdump --uuid "$OUT/$LIB_NAME" 2>/dev/null | awk '{print $2}' | first_line)"
mkdir -p "$TOOLS_DIR/output/staging/$RID"
# gzip rather than xz: gzip is in the box on macOS and xz is not. The Linux staging folder uses
# .xz and the Windows one .zip for the same reason - each platform compresses with what it has.
gzip -9 -c "$OUT/$LIB_NAME" > "$TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
codebrix_llama native library - build information
==============================================================================
RID              : $RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s
Built by         : llama-native-tools/macos/build-osx-x64.sh  ($ROUTE)

Build machine
------------------------------------------------------------------------------
macOS            : $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m))
Compiler         : $CC_VERSION
cmake            : $(cmake --version | first_line) (pinned $CMAKE_VERSION)
ninja            : $(ninja --version) (pinned $NINJA_VERSION)
x86_64 execution : $CAN_RUN

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
llama.cpp        : tag $LLAMA_TAG, commit $LLAMA_COMMIT, ggml $GGML_VERSION
Vendored at      : llama-native-tools/llama.cpp/ (see UPSTREAM.txt)
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
cmake options    : $LLAMA_CMAKE_OPTIONS
arch options     : $LLAMA_CMAKE_OPTIONS_OSX_X64 $EXTRA_CMAKE
CPU baseline     : x86-64 with AVX2, FMA, F16C, BMI2 (no AVX-512)
GPU backend      : none (Metal OFF - CPU-only slice by decision, and ggml-metal returns NaN on Intel GPUs)
Deployment target: MACOSX_DEPLOYMENT_TARGET=$MACOS_MIN_VERSION (checked by the gate)
Wrapper          : llama-native-tools/wrapper/CMakeLists.txt (static archives, whole-archive, one dylib)

Result
------------------------------------------------------------------------------
File             : $LIB_NAME  (unversioned - LibraryImport("$LIBRARY_BASENAME") probes this name)
Install name     : @rpath/$LIB_NAME
Size (stripped)  : $SIZE_STRIPPED bytes
Size (unstripped): $SIZE_UNSTRIPPED bytes -> unstripped/$LIB_NAME
Debug symbols    : $LIB_NAME.dSYM (not shipped)
SHA256           : $SHA
SHA256 unstripped: $SHA_UNSTRIPPED
LC_UUID          : ${UUID:-unknown}
Signature        : ad-hoc (codesign --sign -) - the linker does NOT sign x86_64 output
Dependencies     : $(otool -L "$OUT/$LIB_NAME" | tail -n +3 | awk '{printf "%s ", $1}')
Licence beside it: $LICENSE_FILE_NAME (a verbatim copy of llama.cpp's LICENSE, MIT)

Conformance (test-vectors/codebrix-conformance-tiny.gguf vs EXPECTED.txt, tolerance $CONFORMANCE_TOLERANCE)
------------------------------------------------------------------------------$CONFORMANCE_SUMMARY
EOF

echo "--- done ---"
echo "  $OUT/$LIB_NAME"
echo "  sha256 $SHA"
echo "  staged $TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
echo "  ${ELAPSED}s"
echo
echo "To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt."
