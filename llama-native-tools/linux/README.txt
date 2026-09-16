================================================================================
llama-native-tools/linux - building libcodebrix_llama.so for linux-x64,
                           linux-arm64 and linux-riscv64
================================================================================

>>> STATUS 2026-09-15: ALL THREE SLICES BUILT, GATED AND ADOPTED - on the
    x86_64 LMDE 7 laptop with podman 5.4.2: linux-x64 natively (24 s in the
    container), linux-arm64 and linux-riscv64 under qemu-user emulation (334 s
    and 311 s), exactly the route dav1d-native-tools took. Three things needed
    fixing on the first runs, none of them in build.sh or container-build.sh:
    the wrapper's -Wl,--exclude-libs,ALL (hid every export on ELF), the
    aarch64 Containerfile probe's expected value, and the riscv64 image's
    missing static libstdc++. Each is recorded in BUILD-PROVENANCE.txt under
    "First-run fix". The scripts were written on the Intel Mac mini the same
    day, modelled line by line on CodeBrix.VideoPlayback.Dav1d's
    dav1d-native-tools/linux/, with meson replaced by our CMake wrapper. <<<

WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the three Linux native libraries this package ships,
from the llama.cpp subset vendored in ../llama.cpp/ and our wrapper project in
../wrapper/. Each build is verified before it is allowed to exist in output/.

The build reaches nothing outside this repository. It literally cannot: the
compile runs in a container started with `--network none`, and the first thing
container-build.sh prints is the proof that the container has no network
interface but loopback. The source it compiles, the conformance model it runs
and the expected logits it checks are all files in this repository.

NOTHING HERE INSTALLS ANYTHING ON YOUR MACHINE. Every script checks what it
needs, and if something is missing it names it, prints the command that installs
it, and stops. Installing is your decision.


================================================================================
THE INTENDED ROUTE: ONE NATIVE MACHINE PER ARCHITECTURE
================================================================================
Jeremy has Debian machines for all three architectures. The expected use is:

    on the x64 box:      cd llama-native-tools/linux && ./build.sh x64
    on the arm64 box:    cd llama-native-tools/linux && ./build.sh arm64
    on the riscv64 box:  cd llama-native-tools/linux && ./build.sh riscv64

Each runs at full native speed, and the gate really executes the library on the
hardware it is for. The container is still used on every one of them - not for
emulation, but for the glibc floor (see THE IMAGES below).

The alternative - one x64 host building all three with qemu user-mode emulation,
which is how dav1d-native-tools does it - also works and is supported (see
PREREQUISITES item 2), but llama.cpp is a large C++ code base: dav1d compiles in
about a minute under emulation, and llama.cpp could take the better part of an
hour per architecture. Use it as a fallback, not the plan.


================================================================================
PREREQUISITES  (the complete list - one package, two if emulating)
================================================================================
  1. A container engine - podman (preferred) or docker.

       sudo apt install podman

     Verify:       podman --version

  2. ONLY IF building a foreign architecture on this host: qemu user-mode
     emulation and its binfmt registrations.

       sudo apt install qemu-user-static binfmt-support

     Verify:       ls /proc/sys/fs/binfmt_misc | grep qemu
     Alternative that installs nothing permanently:
       sudo podman run --rm --privileged \
            docker.io/multiarch/qemu-user-static --reset -p yes

  3. Disk: about 2 GB for one base image plus its derived image, and a few
     hundred MB of build tree per run.

  NOT REQUIRED ON THE HOST, AND DELIBERATELY SO: cmake, ninja, gcc, any
  cross-compiler. They live inside the container images, at pinned versions, so
  the build does not vary with whatever the workstation happens to have
  installed this year.


================================================================================
THE IMAGES, AND WHY THEY ARE PINNED BY DIGEST
================================================================================
  RID             base image                          glibc floor   compiler
  --------------  ----------------------------------  -----------   --------
  linux-x64       quay.io/pypa/manylinux_2_28_x86_64   2.28          gcc 14
  linux-arm64     quay.io/pypa/manylinux_2_28_aarch64  2.28          gcc 14
  linux-riscv64   quay.io/pypa/manylinux_2_39_riscv64  2.39          gcc 14

WHY A CONTAINER, EVEN ON A NATIVE MACHINE. glibc symbol versioning is
forward-only: a binary compiled against the glibc on a current desktop distro
refuses to load on anything older. Building on the workstation would quietly
restrict the package to the newest distributions, and the failure would only
appear on a user's machine. The manylinux images are old userlands with modern
compilers, which is exactly the tool for this. The glibc number in the image
name IS the compatibility floor being chosen.

riscv64 has no older manylinux than 2_39, so its floor is glibc 2.39 - Debian 13
/ Ubuntu 24.04 and newer. In practice every riscv64 distribution anyone runs is
newer than that.

WHY DIGESTS. pins.env records a dated tag AND a sha256 digest for each image,
and the Containerfiles resolve the digest. A tag can be moved; a digest cannot.
Without that, an upstream retag would silently change the compiler under a
rebuild and nobody would know why a binary changed. The tag is kept beside it so
a human can see which image generation it is.

The three digests are the same ones CodeBrix.VideoPlayback.Dav1d and
CodeBrix.Audio pin, so every CodeBrix Linux native comes from one base userland.

THE IMAGES ARE THE "INSTALLED TOOLS" EXCEPTION TO THE RULE. Pulling a base image
is the one thing the Linux route fetches from outside the repository - and it is
a compiler, not source. Jeremy accepted that on 2026-09-15. For the day the
images are gone, see THE BARE-HOST ROUTE at the end of this file.


================================================================================
THE DERIVED IMAGE - THE "INSTALL THE TOOLS" STEP
================================================================================
The base images have gcc, binutils and CPython, but not cmake or ninja.
Containerfile.<arch> adds exactly those, at the versions pinned in pins.env
(pip wheels from the image's own CPython), plus `file` and `xz` for the gate
and the staging step, and nothing else:

  Containerfile.x86_64    -> codebrix-llama-build-x86_64
  Containerfile.aarch64   -> codebrix-llama-build-aarch64
  Containerfile.riscv64   -> codebrix-llama-build-riscv64

Building one of these images IS the "install the tools on the build machine"
step that the rule allows, written down as a file instead of as a paragraph
somebody has to follow by hand. It is the only step that uses the network.
build.sh builds an image automatically the first time it needs it and reuses it
afterwards; to force a rebuild:

    FORCE_IMAGE_REBUILD=1 ./build.sh

Each Containerfile ends by PROVING its toolchain rather than trusting a version
string:
  * x86_64  - compiles and runs a C++17 program with -mavx2 -mfma -mf16c -mbmi2.
  * aarch64 - compiles and runs the dotprod and fp16 NEON intrinsics at
              -march=armv8.2-a+dotprod+fp16, the baseline in pins.env.
  * riscv64 - compiles and runs a C++17 program at -march=rv64gc.


================================================================================
USAGE
================================================================================
    cd llama-native-tools/linux
    ./build.sh                  # all three RIDs (only sensible with emulation)
    ./build.sh x64              # or arm64 / riscv64 - the one this machine is

  Environment variables:
    CONTAINER_ENGINE=docker ./build.sh          force an engine
    FORCE_IMAGE_REBUILD=1 ./build.sh            rebuild the derived image first
    MODE=generate-expected ./build.sh x64       write ../output/generated-expected.txt
                                                instead of checking logits. Only
                                                for establishing a new
                                                test-vectors/EXPECTED.txt - see
                                                ../test-vectors/README.txt.

  Timings (2026-09-15, 24-core x86_64 laptop, 24 jobs): linux-x64 24 s inside
  the container natively; linux-arm64 334 s and linux-riscv64 311 s under
  qemu-user emulation; building a derived image the first time adds about one
  minute (x86_64) to two minutes (emulated). For scale, the osx-x64 build on a
  6-core Intel Mac mini took about 4 minutes cold.

  DO NOT RUN TWO COPIES OF build.sh AT ONCE. Both rewrite ../output/SHA256SUMS
  at the end, so a run that finishes while another is still writing its .xz
  will record a hash of a half-written file.

  Output, git-ignored (see ../output/README.txt):
    ../output/<rid>/libcodebrix_llama.so    stripped - the file the package ships
    ../output/<rid>/LICENSE-LlamaCpp.txt    llama.cpp's LICENSE, verbatim
    ../output/<rid>/BUILD-INFO.txt          toolchain, pins, sizes, sha256,
                                            build-id, glibc floor, conformance
    ../output/<rid>/unstripped/libcodebrix_llama.so
                                            for crash triage; never shipped
    ../output/staging/<rid>/libcodebrix_llama.so.xz
                                            compressed, for moving between machines
    ../output/SHA256SUMS                    every artefact, one line each


================================================================================
THE BUILD, IN ONE PARAGRAPH
================================================================================
container-build.sh copies ../llama.cpp to scratch, applies any patches from
../patches (none), and configures ../wrapper/CMakeLists.txt against that copy
with the options from pins.env - LLAMA_CMAKE_OPTIONS plus the architecture's
LLAMA_CMAKE_OPTIONS_<ARCH>. The wrapper builds llama.cpp and ggml as static
archives and links them whole-archive into ONE libcodebrix_llama.so, with
libstdc++ and libgcc linked statically (so the only dynamic dependencies are
glibc's own), a version script that exports only llama_* / ggml_* / gguf_* /
codebrix_llama_*, and -Wl,-z,defs so an undefined symbol fails the link. It also
builds the three gate tools. See ../wrapper/CMakeLists.txt for the reasoning
behind every one of those choices.


================================================================================
THE VERIFICATION GATE
================================================================================
A build that fails ANY of these exits non-zero, removes its staged .xz, and must
not be adopted. A binary that compiles is not necessarily a binary that works.

  1. Architecture - `file` must report x86-64 / ARM aarch64 / UCB RISC-V to
     match the RID. Catches the wrong file being published under the wrong RID.

  2. Required exports - EVERY LLAMA_API function declared in the include/llama.h
     the library was built from (extracted from the header at gate time, so the
     list cannot drift from the source), plus the ggml device-enumeration and
     gguf metadata functions and the two codebrix_llama_* identity functions.
     A missing symbol here is a crash in the field.

  3. Export surface - every exported symbol must start with llama_, ggml_,
     gguf_ or codebrix_llama_. The version script enforces it at link time; the
     gate re-checks the finished file.

  4. Dependencies - `ldd -r` must report no undefined symbols, and the only
     NEEDED libraries may be libc / libm / libpthread / libdl / librt and the
     dynamic loader (pins.env ALLOWED_DEPS). libstdc++ or libgcc_s appearing
     here means the static-runtime link did not take. On glibc 2.34+ images
     libpthread and libdl are folded into libc, so a shorter list there is
     correct, not suspicious.

  5. glibc floor - the highest GLIBC_x.y symbol version referenced, which IS the
     oldest system the binary loads on. CHECKED against pins.env, not merely
     reported: <= 2.28 for x64 and arm64, <= 2.39 for riscv64.

  6. dlopen smoke test - ../smoke-test.c loads the STAGED library (copied beside
     the tool so the shipped bytes are what runs) the way .NET does, resolves 75
     entry points, checks codebrix_llama_rid() equals the RID being built,
     initialises the backend, enumerates devices and prints the CPU-feature line.

  7. Model regeneration - ../test-vectors/generate-test-model, built against
     this library, must reproduce the committed conformance model BYTE FOR BYTE.

  8. Conformance - ../test-vectors/conformance-check runs the committed model
     through this library and every logit must match ../test-vectors/EXPECTED.txt
     within CONFORMANCE_TOLERANCE, with the greedy argmax matching exactly and
     no NaN anywhere. This is the check that says the COMPUTE KERNELS are right
     on this architecture - AVX2 here, NEON there, scalar on riscv64. See
     ../test-vectors/README.txt for how the reference was established.


================================================================================
TROUBLESHOOTING
================================================================================
"neither podman nor docker found"
    Install one (see PREREQUISITES). The script will not install it for you.

"exec format error" / every command in the container dies immediately
    You are building a foreign architecture and the binfmt handler for it is not
    registered. See PREREQUISITES item 2 - or build on the native machine.

Image pull fails / the tag no longer exists
    quay.io/pypa retires old dated tags. Pick a current tag from
    https://quay.io/organization/pypa, put it in pins.env WITH its digest, and
    say in the commit message which glibc floor that changes. Any manylinux_2_28
    or newer image works for x64/arm64. If quay.io itself is gone, see THE
    BARE-HOST ROUTE.

pip cannot find a cmake or ninja wheel for this architecture (riscv64 most likely)
    pip then tries to build them from source, which works but is slow. If it
    fails outright, edit Containerfile.riscv64 to install the image's own
    packages instead (dnf install cmake ninja-build), record the versions that
    gives in BUILD-PROVENANCE.txt, and say so in the commit.

The link fails with "cannot find -lstdc++" or undefined __cxa_* / std:: symbols
    -static-libstdc++ needs the static libstdc++.a. The AlmaLinux 8 images
    (x86_64, aarch64) get it from gcc-toolset-14's libstdc++-devel; the Rocky
    Linux 10 riscv64 image does NOT ship it with the compiler - it is the
    separate libstdc++-static package in the enabled CRB repository, and
    Containerfile.riscv64 installs it (this was the first-run riscv64 failure
    on 2026-09-15). If a future image lacks it, install its libstdc++-static
    package in the Containerfile the same way, or - as a documented deviation -
    drop the two -static-lib* flags in ../wrapper/CMakeLists.txt AND add
    libstdc++.so.6 and libgcc_s.so.1 to ALLOWED_DEPS. The second choice raises
    the package's compatibility bar to the user's distro libstdc++; say so in
    BUILD-PROVENANCE.txt.

(arm64) the gate passes but Q4/Q8 inference is slow on the target board
    The baseline is armv8.2-a+dotprod+fp16. A board without dotprod (Raspberry
    Pi 4 and older) is outside the floor and would in fact CRASH with SIGILL,
    not run slowly; a board with it should be fine. If SIGILL is what you see,
    the floor in pins.env is the thing to lower, at the cost of every arm64
    user's speed.

(riscv64) the gate passes but inference is slow
    Expected: the pins build scalar rv64gc, with no vector code, because RVV is
    compile-time in ggml and would crash a board without V. If every target
    board has RVV 1.0, set GGML_RVV=ON (and the ZFH/ZVFH/ZICBOP/ZIHINTPAUSE
    options) in pins.env, rebuild, and record it in BUILD-PROVENANCE.txt.

Podman "permission denied" writing ../output/
    Rootless podman maps your user into the container and the :Z mount flag
    handles SELinux relabelling. On a system with an unusual security policy,
    try --userns=keep-id.

"pins.env was not found" after a clone
    The repository-root .gitignore has a blanket '*.env' rule, and it also
    ignores directory names that occur inside the vendored llama.cpp tree.
    Both are handled by the "!*" re-include at the top of ../.gitignore - do not
    delete that line. Check with:  git check-ignore -v llama-native-tools/linux/pins.env

A conformance failure with a real, finite max |diff|
    Take it seriously: this build computes differently from the reference. Do
    not raise the tolerance or edit EXPECTED.txt to make it pass. First compare
    the CPU-feature line the smoke test printed with the baseline the pins ask
    for; then read ../test-vectors/README.txt.

A conformance failure reporting NaN logits
    The backend computed garbage. On Linux CPU-only builds this should never
    happen; if it does, the CPU baseline flags are the first suspect.


================================================================================
THE BARE-HOST ROUTE (no container - for the day the images are gone)
================================================================================
The rule this folder exists for is that the libraries can be rebuilt from this
repository alone, years from now. The container images are the one input that
comes from outside, so here is how to build WITHOUT them. The cost is the glibc
floor: a bare-host build's floor is the glibc of the machine that built it,
which must then be recorded in BUILD-PROVENANCE.txt as the compatibility floor
of that binary.

On a Debian-based machine of the target architecture:

    sudo apt install build-essential cmake ninja-build file xz-utils
    cd llama-native-tools
    . linux/pins.env
    rm -rf /tmp/codebrix-llama-src /tmp/codebrix-llama-build
    cp -a llama.cpp /tmp/codebrix-llama-src
    # apply patches/*.patch with `patch -p1` inside /tmp/codebrix-llama-src, if any
    cmake -S wrapper -B /tmp/codebrix-llama-build -G Ninja \
        -DLLAMA_SOURCE_DIR=/tmp/codebrix-llama-src \
        -DCODEBRIX_LLAMA_RID=linux-<x64|arm64|riscv64> \
        -DCODEBRIX_LLAMA_BUILD_INFO="bare-host build, $(date -u +%F), $(cc --version | head -1)" \
        $LLAMA_CMAKE_OPTIONS $LLAMA_CMAKE_OPTIONS_<X64|ARM64|RISCV64>
    cmake --build /tmp/codebrix-llama-build -j "$(nproc)"

Then run steps 4 to 6 of container-build.sh by hand (collect, strip, the gate,
BUILD-INFO) - or simpler, run container-build.sh itself outside a container:

    TARGET_RID=linux-x64 LLAMA_CMAKE_OPTIONS_ARCH="$LLAMA_CMAKE_OPTIONS_X64" \
    GLIBC_MAX=<this machine's glibc, e.g. 2.36> \
    ALLOWED_DEPS="$ALLOWED_DEPS" LLAMA_DIR=$LLAMA_DIR LLAMA_COMMIT=$LLAMA_COMMIT \
    LLAMA_TAG=$LLAMA_TAG GGML_VERSION=$GGML_VERSION LIBRARY_BASENAME=$LIBRARY_BASENAME \
    LLAMA_CMAKE_OPTIONS="$LLAMA_CMAKE_OPTIONS" CMAKE_VERSION=$CMAKE_VERSION \
    NINJA_VERSION=$NINJA_VERSION TEST_VECTOR_DIR=$TEST_VECTOR_DIR \
    TEST_VECTOR_MODEL=$TEST_VECTOR_MODEL TEST_VECTOR_EXPECTED=$TEST_VECTOR_EXPECTED \
    TEST_VECTOR_MODEL_SHA256=$TEST_VECTOR_MODEL_SHA256 \
    CONFORMANCE_TOLERANCE=$CONFORMANCE_TOLERANCE \
    bash -c 'ln -sfn "$PWD" /work 2>/dev/null || true; WORK="$PWD" bash linux/container-build.sh'

(container-build.sh reads /work; a symlink or a bind mount from the folder to
/work makes it run unchanged. It will warn that the host has network interfaces
- the build still fetches nothing - and the glibc-floor check will pass or fail
against the GLIBC_MAX you give it.)

The result is a legitimate build - same source, same options, same gate - whose
only difference from the container route is the glibc floor, and that is why
the floor must be written down with it.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ../output/<rid>/BUILD-INFO.txt and satisfy yourself the build is the
     one you think it is: llama.cpp commit, image digest, glibc floor, the
     conformance line.

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir -p ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native
       cp ../output/<rid>/libcodebrix_llama.so \
          ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/
       cp ../output/<rid>/LICENSE-LlamaCpp.txt \
          ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/

     <rid> is linux-x64, linux-arm64 or linux-riscv64. The licence copy is not
     optional: the MIT licence requires its notice to accompany copies of the
     software, and this is where it travels.

     The file must keep the name libcodebrix_llama.so - unversioned.
     LibraryImport("codebrix_llama") probes exactly that name.

  3. Do NOT copy unstripped/ into the package. It does have a home, and on
     Linux it is stored COMPRESSED (an unstripped llama.cpp ELF is 80-115 MB,
     over GitHub's per-file limit; Jeremy's decision, 2026-09-15):

       xz -T0 -9e -k ../output/<rid>/unstripped/libcodebrix_llama.so
       mkdir -p ../unstripped/<rid>
       mv ../output/<rid>/unstripped/libcodebrix_llama.so.xz ../unstripped/<rid>/
       ( cd ../unstripped && sha256sum <rid>/libcodebrix_llama.so.xz >> SHA256SUMS )

     in the SAME commit that adopts the binary. See ../unstripped/README.txt
     for the rule, the STORED SO FAR entry to add, and the build-id check.

  4. Record the build in ../BUILD-PROVENANCE.txt - copy the values straight out
     of BUILD-INFO.txt.

  5. Run the managed test suite before publishing.


================================================================================
FILES
================================================================================
  README.txt              this document
  pins.env                every version / digest / option pin, FOR ALL THREE
                          PLATFORMS (edit here only)
  build.sh                host entry point: images, emulation check, orchestration
  container-build.sh      the build and the gate; runs inside the container
  Containerfile.x86_64    derived build image for linux-x64
  Containerfile.aarch64   derived build image for linux-arm64
  Containerfile.riscv64   derived build image for linux-riscv64
  ../wrapper/             the CMake project that produces the single library
  ../smoke-test.c         the dlopen verification program, shared by all platforms
  ../test-vectors/        the conformance model, its reference, and the two tools
  ../output/              build results (git-ignored)
================================================================================
