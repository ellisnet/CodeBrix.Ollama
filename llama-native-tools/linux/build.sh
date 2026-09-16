#!/usr/bin/env bash
# ==============================================================================================
# build.sh - build the three Linux codebrix_llama native libraries, from this repository only
# ==============================================================================================
#
#   ./build.sh                build all three (linux-x64, linux-arm64, linux-riscv64)
#   ./build.sh all            the same
#   ./build.sh x64            build linux-x64 only
#   ./build.sh arm64          build linux-arm64 only
#   ./build.sh riscv64        build linux-riscv64 only
#
# Options (environment variables):
#   CONTAINER_ENGINE=podman|docker   force an engine (default: podman, then docker)
#   FORCE_IMAGE_REBUILD=1            rebuild the derived build image even if it exists
#   MODE=generate-expected           run the conformance model and WRITE its logits to
#                                    output/generated-expected.txt instead of comparing them.
#                                    Only for establishing a new test-vectors/EXPECTED.txt;
#                                    a normal build must never use it.
#
#   NEVER YET RUN. Written on the Intel Mac mini on 2026-09-15 (no container engine there);
#   modelled line by line on CodeBrix.VideoPlayback.Dav1d's dav1d-native-tools/linux/build.sh,
#   which has run on all three architectures. Expect to fix something on the first real run;
#   fix it IN THE SCRIPT and commit that. Then rewrite this header and README.txt.
#
# WHAT THIS DOES, IN ONE PARAGRAPH
#   For each architecture it makes sure a derived build image exists (base manylinux image +
#   cmake + ninja - see Containerfile.<arch>; building it is the one step that uses the
#   network), then runs container-build.sh inside it WITH THE NETWORK DISABLED, mounting
#   llama-native-tools/ as /work. Everything compiled comes from this repository: the vendored
#   llama.cpp subset in llama.cpp/, the wrapper project in wrapper/, and the conformance assets
#   in test-vectors/.
#
# WHY A CONTAINER, EVEN FOR X64, EVEN ON NATIVE HARDWARE
#   glibc symbol versioning is forward-only: a binary built against the glibc on a current
#   desktop distro refuses to load on anything older, so building on the workstation would
#   quietly restrict the package to the newest distributions - and the failure would only show
#   up on a user's machine. The manylinux images fix the floor at glibc 2.28 (2.39 for riscv64,
#   where nothing older exists). Every Linux RID is built this way. Jeremy has native x64, arm64
#   and riscv64 Debian machines, so the expected use is: run this script ON EACH, for its own
#   architecture, with no emulation involved. qemu user-mode emulation is supported as the
#   fallback for building a foreign architecture on one x64 host - but llama.cpp is a large
#   C++ code base and an emulated build can take the better part of an hour.
#
# THIS SCRIPT NEVER INSTALLS ANYTHING ON YOUR MACHINE. If a prerequisite is missing it names it,
# prints the command that installs it, and stops. See README.txt for the full list.
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: build.sh failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOLS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"           # llama-native-tools/
OUTPUT_DIR="$TOOLS_DIR/output"

# shellcheck source=pins.env
. "$SCRIPT_DIR/pins.env"

MODE="${MODE:-build}"

# ----------------------------------------------------------------------------------------------
# Prerequisites - checked, never installed
# ----------------------------------------------------------------------------------------------
pick_engine() {
    if [ -n "${CONTAINER_ENGINE:-}" ]; then
        command -v "$CONTAINER_ENGINE" > /dev/null 2>&1 || {
            echo "ERROR: CONTAINER_ENGINE=$CONTAINER_ENGINE is not on PATH." >&2; exit 1; }
        echo "$CONTAINER_ENGINE"
        return
    fi
    if command -v podman > /dev/null 2>&1; then echo podman; return; fi
    if command -v docker > /dev/null 2>&1; then echo docker; return; fi
    cat >&2 <<'EOF'
ERROR: neither podman nor docker is installed.

  This script does not install anything. Install a container engine yourself:

    Debian-based Linux:  sudo apt install podman
    Fedora/RHEL:         sudo dnf install podman

  Then re-run. See README.txt (PREREQUISITES) for the complete list, and
  "THE BARE-HOST ROUTE" there for building with no container at all.
EOF
    exit 1
}

ENGINE="$(pick_engine)"

[ -f "$TOOLS_DIR/$LLAMA_DIR/CMakeLists.txt" ] && [ -f "$TOOLS_DIR/$LLAMA_DIR/ggml/CMakeLists.txt" ] || {
    echo "ERROR: the vendored llama.cpp source is missing from $TOOLS_DIR/$LLAMA_DIR." >&2
    echo "       Nothing can be built without it, and it is not downloaded - it is part of this" >&2
    echo "       repository. Restore it from git." >&2
    exit 1; }
[ -f "$TOOLS_DIR/wrapper/CMakeLists.txt" ] || { echo "ERROR: $TOOLS_DIR/wrapper/CMakeLists.txt is missing; it is part of the repository." >&2; exit 1; }
[ -f "$TOOLS_DIR/$TEST_VECTOR_MODEL" ] || {
    echo "ERROR: the conformance model $TEST_VECTOR_MODEL is missing. The conformance gate cannot" >&2
    echo "       run without it, and a build that skips conformance is not a build worth shipping." >&2
    exit 1; }
if [ "$MODE" != "generate-expected" ] && [ ! -f "$TOOLS_DIR/$TEST_VECTOR_EXPECTED" ]; then
    echo "ERROR: the expected-logits file $TEST_VECTOR_EXPECTED is missing. See test-vectors/README.txt." >&2
    exit 1
fi

host_machine="$(uname -m)"

arch_rid()      { case "$1" in x64) echo linux-x64;; arm64) echo linux-arm64;; riscv64) echo linux-riscv64;; esac; }
arch_platform() { case "$1" in x64) echo linux/amd64;; arm64) echo linux/arm64;; riscv64) echo linux/riscv64;; esac; }
arch_native()   { case "$1" in x64) echo x86_64;; arm64) echo aarch64;; riscv64) echo riscv64;; esac; }
arch_image()    {
    case "$1" in
        x64)     echo "${IMAGE_X64%%:*}@$IMAGE_X64_DIGEST" ;;
        arm64)   echo "${IMAGE_ARM64%%:*}@$IMAGE_ARM64_DIGEST" ;;
        riscv64) echo "${IMAGE_RISCV64%%:*}@$IMAGE_RISCV64_DIGEST" ;;
    esac
}
arch_tag()      {
    case "$1" in x64) echo "$IMAGE_X64";; arm64) echo "$IMAGE_ARM64";; riscv64) echo "$IMAGE_RISCV64";; esac
}
arch_options()  {
    case "$1" in x64) echo "$LLAMA_CMAKE_OPTIONS_X64";; arm64) echo "$LLAMA_CMAKE_OPTIONS_ARM64";; riscv64) echo "$LLAMA_CMAKE_OPTIONS_RISCV64";; esac
}
arch_glibc_max() {
    case "$1" in x64) echo "$GLIBC_MAX_X64";; arm64) echo "$GLIBC_MAX_ARM64";; riscv64) echo "$GLIBC_MAX_RISCV64";; esac
}

check_emulation() {
    local arch="$1" want
    want="$(arch_native "$arch")"
    [ "$want" = "$host_machine" ] && { echo "  emulation : none needed - native $want host"; return 0; }

    # Non-native architecture: the kernel needs a binfmt_misc handler registered for it,
    # otherwise the container starts and every command inside it dies with "exec format error".
    # grep WITHOUT -q on purpose: -q exits at the first match, `ls` then dies of SIGPIPE, and
    # under `set -o pipefail` that reads as "no handler registered".
    if [ -d /proc/sys/fs/binfmt_misc ] && ls /proc/sys/fs/binfmt_misc 2>/dev/null | grep -i "qemu-$want" > /dev/null; then
        echo "  emulation : qemu-$want binfmt handler registered (expect a SLOW build - llama.cpp is large)"
        return 0
    fi
    cat >&2 <<EOF

ERROR: building $arch on a $host_machine host needs qemu user-mode emulation, and no
       binfmt handler for qemu-$want is registered.

  The intended route is to run this script on a native $want machine. If you must build
  here instead, set up emulation ONE of these ways (neither is done for you):

    1. Install the static qemu binaries and their binfmt registrations:
         sudo apt install qemu-user-static binfmt-support

    2. Or register the handlers from a container, installing nothing permanently:
         sudo $ENGINE run --rm --privileged docker.io/multiarch/qemu-user-static --reset -p yes

  Verify with:  ls /proc/sys/fs/binfmt_misc | grep qemu

EOF
    exit 1
}

ensure_image() {
    local arch="$1" narch derived base platform
    narch="$(arch_native "$arch")"
    derived="$DERIVED_IMAGE_PREFIX-$narch"
    base="$(arch_image "$arch")"
    platform="$(arch_platform "$arch")"

    if [ "${FORCE_IMAGE_REBUILD:-0}" != "1" ] && "$ENGINE" image exists "$derived" 2>/dev/null; then
        echo "  image     : $derived (already built - reusing)"
        return 0
    fi

    echo "  image     : $derived - building it now."
    echo "              THIS IS THE ONE STEP THAT USES THE NETWORK: it installs cmake"
    echo "              ${CMAKE_VERSION} and ninja ${NINJA_VERSION} into the digest-pinned base image."
    echo "              The library build afterwards runs with --network none."
    "$ENGINE" build \
        --platform "$platform" \
        -f "$SCRIPT_DIR/Containerfile.$narch" \
        -t "$derived" \
        --build-arg BASE_IMAGE="$base" \
        --build-arg CMAKE_VERSION="$CMAKE_VERSION" \
        --build-arg NINJA_VERSION="$NINJA_VERSION" \
        --build-arg PYTHON_IN_IMAGE="$PYTHON_IN_IMAGE" \
        "$SCRIPT_DIR"
}

build_arch() {
    local arch="$1" rid narch derived base platform started elapsed
    rid="$(arch_rid "$arch")"
    narch="$(arch_native "$arch")"
    derived="$DERIVED_IMAGE_PREFIX-$narch"
    base="$(arch_image "$arch")"
    platform="$(arch_platform "$arch")"
    started="$SECONDS"

    echo
    echo "=============================================================================="
    echo " BUILD $rid"
    echo "=============================================================================="
    echo "  base tag  : $(arch_tag "$arch")"
    echo "  base pin  : $base"
    echo "  platform  : $platform"
    echo "  host      : $host_machine"
    check_emulation "$arch"
    ensure_image "$arch"
    echo "  network   : DISABLED for the build itself (--network none)"
    echo

    "$ENGINE" run --rm \
        --network none \
        --platform "$platform" \
        -v "$TOOLS_DIR":/work:Z \
        -e TARGET_RID="$rid" \
        -e DERIVED_IMAGE="$derived" \
        -e BASE_IMAGE_REF="$base" \
        -e MODE="$MODE" \
        -e LLAMA_DIR="$LLAMA_DIR" \
        -e LLAMA_COMMIT="$LLAMA_COMMIT" \
        -e LLAMA_TAG="$LLAMA_TAG" \
        -e GGML_VERSION="$GGML_VERSION" \
        -e LIBRARY_BASENAME="$LIBRARY_BASENAME" \
        -e LLAMA_CMAKE_OPTIONS="$LLAMA_CMAKE_OPTIONS" \
        -e LLAMA_CMAKE_OPTIONS_ARCH="$(arch_options "$arch")" \
        -e CMAKE_VERSION="$CMAKE_VERSION" \
        -e NINJA_VERSION="$NINJA_VERSION" \
        -e GLIBC_MAX="$(arch_glibc_max "$arch")" \
        -e ALLOWED_DEPS="$ALLOWED_DEPS" \
        -e TEST_VECTOR_DIR="$TEST_VECTOR_DIR" \
        -e TEST_VECTOR_MODEL="$TEST_VECTOR_MODEL" \
        -e TEST_VECTOR_EXPECTED="$TEST_VECTOR_EXPECTED" \
        -e TEST_VECTOR_MODEL_SHA256="$TEST_VECTOR_MODEL_SHA256" \
        -e CONFORMANCE_TOLERANCE="$CONFORMANCE_TOLERANCE" \
        "$derived" \
        bash /work/linux/container-build.sh

    elapsed=$(( SECONDS - started ))
    echo "  $rid finished in ${elapsed}s"
}

write_sha256sums() {
    # One checksum file covering every built RID, including the ones built on other machines
    # (Windows, macOS) if their output has been copied in. Paths are relative to output/.
    [ -d "$OUTPUT_DIR" ] || return 0
    ( cd "$OUTPUT_DIR" && \
      find . -type f \( -name "lib${LIBRARY_BASENAME}.so" -o -name "lib${LIBRARY_BASENAME}.dylib" -o -name "${LIBRARY_BASENAME}.dll" -o -name '*.xz' \) \
        | sed 's|^\./||' | sort | xargs -r sha256sum > SHA256SUMS )
    echo
    echo "  output/SHA256SUMS:"
    sed 's/^/    /' "$OUTPUT_DIR/SHA256SUMS"
}

# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------
TARGET="${1:-all}"

echo "container engine : $ENGINE"
echo "tools folder     : $TOOLS_DIR"
echo "llama.cpp        : tag $LLAMA_TAG, commit $LLAMA_COMMIT, ggml $GGML_VERSION"
echo "cmake / ninja    : $CMAKE_VERSION / $NINJA_VERSION (pinned, inside the images)"
echo "mode             : $MODE"

case "$TARGET" in
    x64|arm64|riscv64) build_arch "$TARGET" ;;
    all)               for a in x64 arm64 riscv64; do build_arch "$a"; done ;;
    *) echo "usage: $0 [all|x64|arm64|riscv64]" >&2; exit 2 ;;
esac

write_sha256sums

echo
echo "=============================================================================="
echo " Outputs are in llama-native-tools/output/<rid>/"
echo "   lib${LIBRARY_BASENAME}.so   stripped, the file the package ships"
echo "   LICENSE-LlamaCpp.txt        llama.cpp's LICENSE (MIT), shipped beside it"
echo "   BUILD-INFO.txt              toolchain, pins, sizes, sha256, glibc floor, conformance"
echo "   unstripped/                 keep for crash triage; never shipped"
echo " Compressed copies:  output/staging/<rid>/lib${LIBRARY_BASENAME}.so.xz"
echo " To adopt them into the package, follow ADOPTING A BUILT BINARY in README.txt."
echo "=============================================================================="
