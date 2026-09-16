#!/usr/bin/env bash
# ==============================================================================================
# build-osx-arm64.sh - build libcodebrix_llama.dylib for the osx-arm64 runtime identifier
# ==============================================================================================
#
#   NEVER YET RUN. Written on the Intel Mac mini on 2026-09-15, where it cannot execute (it
#   refuses to run on anything but an Apple Silicon Mac). Everything it does that differs from
#   build-osx-x64.sh - the arm64 baseline flag, Metal ON, the Metal conformance pass - is an
#   assumption until the first real run. Expect to fix something; fix it IN THE SCRIPT and
#   commit that. Then rewrite this header and README.txt with what the run established.
#
# USAGE
#     cd llama-native-tools/macos
#     ./build-osx-arm64.sh
#
# A native build on an Apple Silicon Mac. The osx-x64 slice can come from the SAME machine via
# build-osx-x64.sh (cross route, Rosetta 2 to verify) or from an Intel Mac.
#
# THIS SLICE CARRIES METAL. The gate therefore runs the conformance model TWICE - on the CPU
# path and with every layer offloaded to the GPU - and both must match the reference within
# tolerance. The Metal library (shaders) is EMBEDDED in the dylib (GGML_METAL_EMBED_LIBRARY=ON),
# so no .metallib file ships beside it; the shaders compile at first load on the user's Mac.
#
# Output: ../output/osx-arm64/
# It installs nothing: anything missing is named with the command that installs it.
# ==============================================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=build-common.sh
. "$SCRIPT_DIR/build-common.sh"

RID=osx-arm64
ARCH=arm64
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
# 1. Prerequisites
# ----------------------------------------------------------------------------------------------
echo "--- prerequisites ---"
check_common_prerequisites
if [ "$(uname -m)" != "arm64" ]; then
    echo "ERROR: this script builds the arm64 slice natively and must run on an Apple Silicon Mac." >&2
    echo "       This machine reports $(uname -m). (There is no cross route for this slice: the" >&2
    echo "       gate has to RUN the Metal backend, and only Apple Silicon can.)" >&2
    exit 1
fi
CC_VERSION="$(cc --version | first_line)"
echo "  cc       : $CC_VERSION"
echo "  cmake    : $(cmake --version | first_line) (pinned $CMAKE_VERSION)"
echo "  ninja    : $(ninja --version) (pinned $NINJA_VERSION)"
echo "  min macOS: $MACOS_MIN_VERSION"
echo "  GPU      : $(system_profiler SPDisplaysDataType 2>/dev/null | awk -F': ' '/Chipset Model/{print $2; exit}')"
echo

# ----------------------------------------------------------------------------------------------
# 2. Source + build
# ----------------------------------------------------------------------------------------------
echo "--- source ---"
copy_source_to_scratch "$SCRATCH"
echo

echo "--- building ---"
build_library "$SCRATCH" "$BUILD" "$RID" "$LLAMA_CMAKE_OPTIONS_OSX_ARM64" ""
echo

# ----------------------------------------------------------------------------------------------
# 3. Collect
# ----------------------------------------------------------------------------------------------
echo "--- collecting ---"
collect_dylib "$BUILD" "$OUT"
echo

# ----------------------------------------------------------------------------------------------
# 4. The gate - including the Metal conformance pass
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
verify_dylib "$OUT/$LIB_NAME" "$ARCH" "$BUILD" yes yes

if [ "$GATE_FAILED" -ne 0 ]; then
    echo
    echo "VERIFICATION FAILED for $RID. $OUT is left for inspection, but this build must not be"
    echo "adopted into the package."
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
gzip -9 -c "$OUT/$LIB_NAME" > "$TOOLS_DIR/output/staging/$RID/$LIB_NAME.gz"
ELAPSED=$(( SECONDS - START_SECONDS ))

cat > "$OUT/BUILD-INFO.txt" <<EOF
codebrix_llama native library - build information
==============================================================================
RID              : $RID
Built            : $STARTED_AT
Build duration   : ${ELAPSED}s
Built by         : llama-native-tools/macos/build-osx-arm64.sh (native build)

Build machine
------------------------------------------------------------------------------
macOS            : $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m))
GPU              : $(system_profiler SPDisplaysDataType 2>/dev/null | awk -F': ' '/Chipset Model/{print $2; exit}')
Compiler         : $CC_VERSION
cmake            : $(cmake --version | first_line) (pinned $CMAKE_VERSION)
ninja            : $(ninja --version) (pinned $NINJA_VERSION)

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
llama.cpp        : tag $LLAMA_TAG, commit $LLAMA_COMMIT, ggml $GGML_VERSION
Vendored at      : llama-native-tools/llama.cpp/ (see UPSTREAM.txt)
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
cmake options    : $LLAMA_CMAKE_OPTIONS
arch options     : $LLAMA_CMAKE_OPTIONS_OSX_ARM64
CPU baseline     : armv8.2-a + dotprod + fp16
GPU backend      : Metal (shader library embedded in the dylib)
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
Signature        : ad-hoc (codesign --sign -)
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
