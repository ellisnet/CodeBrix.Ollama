================================================================================
llama-native-tools/macos - building libcodebrix_llama.dylib for osx-x64 and
                           osx-arm64
================================================================================

>>> STATUS <<<
--------------------------------------------------------------------------------
  osx-x64     RUN AND VERIFIED on 2026-09-15 on the Intel Mac mini (native
              route): macOS 15.8, Apple clang 17.0.0, cmake 4.4.3, ninja
              1.13.2. Built clean and passed the full gate; adopted into the
              package. Four consecutive runs established that the slice is
              reproducible except for its LC_UUID - see WHAT HAS AND HAS NOT
              BEEN VERIFIED. The CROSS route (building osx-x64 on an Apple
              Silicon Mac) has NOT been run.
              >>> REBUILD REQUIRED. That build carries the old 11.0 floor,
              which the osx-arm64 run later showed to be false for BOTH
              slices (THE MINIMUM macOS VERSION, below): it weak-imports a
              13.3-only Accelerate symbol and would crash on macOS 11.0-13.2.
              The shipped file stays until it is rebuilt at 13.3 with this
              same script; the wrapper now refuses to build otherwise. <<<

  osx-arm64   RUN AND VERIFIED on 2026-09-15 on the Apple Silicon Mac mini
              (M2 Pro, 10 cores): macOS 27.0, Apple clang 21.0.0, cmake
              4.4.3, ninja 1.13.2. The script ran UNCHANGED and passed all
              eleven gate checks on its first run - but the build log's three
              compiler warnings showed the 11.0 floor was never real. The
              floor was raised to 13.3 in pins.env, the wrapper now fails the
              build on any unguarded use of a newer API, and the second run
              (30 s cold, zero warnings) passed the full gate including the
              Metal conformance pass and was adopted into the package.


WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the two macOS native libraries this package ships,
from the llama.cpp subset vendored in ../llama.cpp/ and our wrapper project in
../wrapper/. Nothing is downloaded: not the source, not the conformance model,
not the expected logits. The only things from outside are the tools you install
on the Mac, listed below with the command that installs each one.

  osx-x64     CPU only (Metal OFF)         ./build-osx-x64.sh
              native on an Intel Mac, or cross-built on an Apple Silicon Mac
  osx-arm64   CPU + Metal                  ./build-osx-arm64.sh
              native on an Apple Silicon Mac only

They stay TWO SEPARATE DYLIBS in two separate RID folders - deliberately not a
universal binary. The package's runtimes/osx-arm64/native/ and
runtimes/osx-x64/native/ folders each want their own file; a fat binary would
put both slices in both places and double the size of each for nothing.


================================================================================
PREREQUISITES
================================================================================
  1. Xcode Command Line Tools (clang, otool, nm, strip, install_name_tool,
     codesign, dsymutil, dwarfdump).

       xcode-select --install

     Verify: cc --version     (should say "Apple clang")

  2. cmake and ninja.

       brew install cmake ninja

     Verify: cmake --version   ninja --version
     The versions pinned in ../linux/pins.env (cmake 4.4.3, ninja 1.13.2 as of
     2026-09-15) are what the osx-x64 build used - Homebrew happened to match
     both exactly that day. Newer is fine - the wrapper needs cmake >= 3.24 -
     and BUILD-INFO.txt records what was actually used, so a difference is
     never invisible.

     On an Intel Mac Homebrew is "Tier 3" (unsupported) as of 2026 and some
     formulae compile from source; cmake, ninja and ccache all installed fine
     on 2026-09-15.

  3. ccache - OPTIONAL. Not used by the scripts (a gate build must not depend on
     a cache), but harmless if installed.

  4. Rosetta 2 - FOR THE osx-x64 CROSS ROUTE ONLY, and only to VERIFY, not to
     build. On an Apple Silicon Mac the gate has to RUN x86_64 code: the smoke
     test, the model regeneration and the conformance check. Without Rosetta
     those checks cannot run, and build-osx-x64.sh reports them as FAILURES
     rather than skipping them quietly.

       softwareupdate --install-rosetta

  5. Homebrew itself, if you do not already have it: https://brew.sh


================================================================================
USAGE
================================================================================
    cd llama-native-tools/macos
    ./build-osx-x64.sh          # about 4 minutes cold on a 6-core Intel mini
    ./build-osx-arm64.sh        # Apple Silicon only; timing unknown

  Each script is self-contained and idempotent: it removes its own scratch and
  build directories on every run, so re-running is always safe and never needs
  a manual clean. Neither installs anything.

  Output, git-ignored:
    ../output/<rid>/libcodebrix_llama.dylib     the file the package ships
    ../output/<rid>/libcodebrix_llama.dylib.dSYM
                                                debug symbols. NOT shipped
    ../output/<rid>/unstripped/libcodebrix_llama.dylib
                                                the pre-strip binary
    ../output/<rid>/LICENSE-LlamaCpp.txt        llama.cpp's LICENSE, verbatim
    ../output/<rid>/BUILD-INFO.txt              toolchain, pins, sizes, sha256,
                                                LC_UUID, deployment target,
                                                conformance
    ../output/staging/<rid>/libcodebrix_llama.dylib.gz
                                                compressed copy for moving to
                                                whichever machine assembles the
                                                package (.gz because gzip is in
                                                the box on macOS and xz is not)


================================================================================
THE BUILD, IN ONE PARAGRAPH
================================================================================
build-common.sh copies ../llama.cpp to /tmp, applies any patches from
../patches (none), and configures ../wrapper/CMakeLists.txt against that copy
with the options from pins.env - LLAMA_CMAKE_OPTIONS plus the slice's
LLAMA_CMAKE_OPTIONS_OSX_X64 or _OSX_ARM64 - and MACOSX_DEPLOYMENT_TARGET. The
wrapper builds llama.cpp and ggml as static archives and links them with
-force_load into ONE libcodebrix_llama.dylib whose exported symbols are limited
by ../wrapper/exports-macos.txt to llama_* / ggml_* / gguf_* /
codebrix_llama_*. It also builds the three gate tools. Then: dsymutil, strip -x,
install_name_tool -id @rpath/..., codesign --sign -, in that order.


================================================================================
THE MINIMUM macOS VERSION, AND WHY IT IS 13.3
================================================================================
A Mach-O binary records the oldest macOS it will run on. With no explicit
deployment target, clang stamps in the version of the machine doing the
building - so a dylib built on a current Mac is refused by dyld on every older
one. The only fix is to state the floor explicitly.

Both scripts pass CMAKE_OSX_DEPLOYMENT_TARGET=$MACOS_MIN_VERSION from pins.env
(13.3) and export MACOSX_DEPLOYMENT_TARGET, and the gate then CHECKS the built
file really carries it (otool -l, LC_BUILD_VERSION minos) rather than merely
reporting it.

BUT A STAMP IS NOT A FLOOR. The stamp only says which macOS dyld will accept
the file on; it says nothing about whether every symbol the code calls exists
there. When code calls an API newer than the deployment target without an
@available guard, clang warns (-Wunguarded-availability-new) and the LINKER
makes the symbol a WEAK import: on an older macOS the library loads, the
symbol resolves to NULL, and the first call crashes. The 11.0 floor this
folder started with on 2026-09-15 (chosen as the oldest macOS that exists for
Apple Silicon) was exactly that. The first osx-arm64 build's log carried three
such warnings, and `nm -m` on BOTH slices - the adopted osx-x64 file included -
showed

    (undefined) weak external _cblas_sgemm$NEWLAPACK$ILP64 (from Accelerate)

ggml-blas/CMakeLists.txt defines ACCELERATE_NEW_LAPACK and
ACCELERATE_LAPACK_ILP64 for the Apple vendor, which selects Accelerate's new
LAPACK interface, available from macOS 13.3. ggml-metal additionally calls
-[MTLSharedEvent waitUntilSignaledValue:timeoutMS:], available from macOS
12.0, unguarded. So an "11.0" dylib loads on 11.0-13.2 and crashes at the
first BLAS matmul (any prompt of 32 or more tokens) or the first cross-backend
event wait. The gate could not see it: it checks the minos stamp and the
dependency list, not whether the referenced symbols exist at that version.

The fix has two halves, both from 2026-09-15:
  * MACOS_MIN_VERSION=13.3 in pins.env - the smallest version at which every
    symbol the library references is real. Both slices, one floor. Current
    .NET releases require macOS 13 or newer, so no user is lost.
  * ../wrapper/CMakeLists.txt compiles every macOS translation unit (C, C++,
    Objective-C) with -Werror=unguarded-availability-new, so the floor is
    PROVEN by the build: any future use of an API newer than pins.env's value
    fails to compile instead of failing on a user's Mac. Properly guarded
    uses - ggml-metal wraps its macOS-15 residency sets in @available - still
    compile, and still show as weak imports, which is correct. Verified on
    the arm64 slice: zero warnings at 13.3, and cblas_sgemm$NEWLAPACK$ILP64
    became a normal (non-weak) import.

If a future llama.cpp snapshot calls something newer still, the build will
say so. Then either raise MACOS_MIN_VERSION (both slices, rebuild both, record
it in BUILD-PROVENANCE.txt) or, if the call is in a path that can be avoided,
guard it with a patch in ../patches/. Never silence the error.


================================================================================
THE VERIFICATION GATE
================================================================================
A build that fails any check exits non-zero and must not be adopted. Eleven
checks on the finished file; the last is arm64-only. One more happens at
compile time: the wrapper's -Werror=unguarded-availability-new fails the build
on any unguarded use of an API newer than the floor (THE MINIMUM macOS
VERSION, above) - the check the eleven below cannot make.

  1. Architecture - `file` must report x86_64 or arm64, matching the RID. One
     machine can build both slices, so this catches the wrong file being
     published under the wrong RID.

  2. Required exports - EVERY LLAMA_API function in the include/llama.h the
     library was built from (extracted from the header at gate time, comment
     lines dropped, so the list cannot drift: 233 functions on 2026-09-15),
     plus the ggml device-enumeration and gguf metadata functions and the two
     codebrix_llama_* identity functions. 248 names in all.

  3. Export surface - every exported symbol must start with _llama_, _ggml_,
     _gguf_ or _codebrix_llama_. exports-macos.txt enforces it at link time;
     the gate re-checks the finished file (1,159 symbols on 2026-09-15).

  4. Install name - `otool -D` must report @rpath/libcodebrix_llama.dylib.

  5. Dependencies - `otool -L` may list only system libraries: libSystem,
     libc++ and Accelerate for the CPU slice; plus libobjc, Foundation,
     CoreFoundation, Metal and MetalKit for the Metal slice. Anything else
     means the package would demand a library be installed on the user's Mac.

  6. Deployment target - CHECKED against pins.env's MACOS_MIN_VERSION (13.3),
     not merely reported.

  7. Code signature - `codesign -dv` must find one. Apple Silicon refuses to
     load an unsigned dylib outright. Note the ORDER in the scripts: strip,
     then install_name_tool, then codesign. The first two invalidate a
     signature, so signing has to be last - and the linker's own ad-hoc
     signature (which it applies to arm64 output but NOT to x86_64) is gone by
     then either way.

  8. dlopen smoke test - ../smoke-test.c loads the STAGED dylib (copied into
     the build directory, where the gate tools resolve it through
     @loader_path, so the shipped bytes are what runs) the way .NET does,
     resolves 75 entry points, checks codebrix_llama_rid() equals the RID being
     built, initialises the backend, enumerates devices and prints the
     CPU-feature line.

  9. Model regeneration - generate-test-model, built against this library,
     must reproduce the committed conformance model BYTE FOR BYTE.

 10. Conformance, CPU - conformance-check runs the committed model on the CPU
     path and every logit must match ../test-vectors/EXPECTED.txt within the
     tolerance, argmax exact, no NaN.

 11. Conformance, Metal (osx-arm64 only) - the same check with every layer
     offloaded to the GPU. REQUIRED to pass on the arm64 slice. If this ever
     reports NaN logits, the GPU is not supported by ggml-metal - which is
     exactly what happened on the Intel UHD 630 on 2026-09-15, and why the
     x64 slice carries no Metal at all.

  For the osx-x64 cross route on Apple Silicon, checks 8-10 need Rosetta 2.
  Without it they are reported as FAILURES, never as passes or skips.


================================================================================
WHAT HAS AND HAS NOT BEEN VERIFIED
================================================================================
Established by the osx-x64 runs on 2026-09-15 (Intel Mac mini, macOS 15.8):

  * The wrapper's static-archive + -force_load approach produces one dylib of
    4,221,552 bytes (stripped and signed) with exactly the intended dependency list and
    export surface. All 233 header-declared functions are exported.
  * The gate's export extraction copes with upstream's one commented-out
    LLAMA_API declaration (llama_decode_with_sampler) - comment lines are
    dropped before matching, which is why it is not on the required list.
  * The model generator is deterministic on this platform: two runs in one
    build and every run across four builds produced the same 101,056-byte
    file, sha256 66c90c32... (pins.env).
  * The CPU-path conformance spread against the reference is 4.98e-08 - five
    orders of magnitude inside the 1e-3 tolerance.
  * Cold build about 4 minutes; with a warm ccache under 1 minute (the scripts
    do not use ccache; the trial builds that preceded them did).
  * Metal on an Intel GPU: ggml-metal initialises on the UHD 630 and then
    returns NaN for every logit. Not a defect of this tooling - upstream's
    Metal backend targets Apple Silicon - but it is the reason (beyond size)
    that osx-x64 is built with GGML_METAL=OFF, and the reason the conformance
    checker fails on non-finite values.

Established by the osx-arm64 runs on 2026-09-15 (Apple Silicon Mac mini,
M2 Pro, macOS 27.0, Xcode 27.0 / Apple clang 21.0.0):

  * The script ran unchanged on its first run and passed all eleven checks:
    the arm64 baseline flag, Metal ON with the embedded shader library, the
    Metal conformance pass and the arm64 export check all did what they were
    written to do. What needed fixing was in the build log, not the gate:
    three -Wunguarded-availability-new warnings, the floor problem described
    above. The adopted file is the SECOND run, at 13.3, with zero warnings.
  * One dylib of 4,307,696 bytes (stripped and signed), 1,392 exported
    symbols, all 233 header-declared functions present, exactly the Metal
    dependency list (Accelerate, libSystem, Foundation, CoreFoundation,
    Metal, MetalKit, libc++, libobjc).
  * The smoke test enumerates three devices: MTL0 (Apple M2 Pro), BLAS
    (Accelerate) and CPU. System info: NEON, ARM_FMA, FP16_VA, DOTPROD,
    ACCELERATE, REPACK, and MTL EMBED_LIBRARY.
  * The embedded Metal shader library compiled at first load in about 9
    seconds on the M2 Pro (ggml_metal_library_init: "loaded in 9.102 sec" on
    the first run). macOS caches the compiled shaders: the second run's load
    took 0.028 s. So the cost is paid once per machine per library build,
    not once per process - worth knowing when a FIRST inference looks slow.
  * Conformance against the AVX2-generated reference: max |diff| 1.43e-04 on
    the CPU (NEON) path and 1.14e-04 on the Metal path - the first GPU
    verification of EXPECTED.txt, and the first non-x86 one. Argmax exact at
    every position, no NaN. Both spreads are three orders of magnitude LARGER
    than the x64 CPU path's 4.98e-08 (that was AVX2 checking AVX2) and about
    seven times inside the 1e-3 tolerance. This is the cross-architecture
    rounding spread the tolerance was chosen for; if a later RID lands much
    closer to 1e-3, look before accepting it.
  * The model generator reproduced the committed conformance model byte for
    byte on arm64, so the gguf writer and the seeded arithmetic are
    platform-independent as intended.
  * Cold build 30 seconds on 10 cores (44 s on the first run, which paid the
    Metal shader compile inside the gate).
  * dsymutil did NOT emit the duplicate-object-name warning on this slice.
  * The linker ad-hoc signs arm64 output, as gate item 7 predicted: codesign
    prints "replacing existing signature" during collection. Expected.
  * The .dSYM is 57 MB; the unstripped dylib is 5,524,600 bytes.

TWO THINGS THE RUNS TURNED UP THAT ARE WORTH KNOWING
--------------------------------------------------------------------------------
1. osx-x64 IS NOT BYTE-REPRODUCIBLE, AND THE DIFFERENCE IS ONLY THE UUID.

   Measured over four consecutive from-scratch runs (two without and two with
   ZERO_AR_DATE=1, which changed nothing). The UNSTRIPPED dylibs of two runs
   differ in exactly 18 bytes: the 16-byte LC_UUID payload at file offset
   1657-1672, and 2 bytes at offset 3519848-3519849 inside the symbol-table
   region. The STRIPPED, signed files differ in 112 bytes: the same UUID plus
   the ad-hoc signature that necessarily changes because it hashes the UUID.
   Every byte of code and data is identical, and every run passes the whole
   gate. ld64 derives the content-based UUID from the pre-strip image, which
   with -g carries per-object debug-map entries (N_OSO) that record the object
   files' modification times - new on every build.

   WHY IT IS LEFT ALONE. Dropping the UUID (-Wl,-no_uuid) would make crash
   reports from shipped binaries much harder to symbolicate, which is the
   whole reason the unstripped twin and dSYM are kept. What matters is that
   the claim in ../BUILD-PROVENANCE.txt is accurate: osx-x64 is reproducible
   EXCEPT for LC_UUID. If you rebuild and get a different sha256 from the one
   recorded, confirm it is only that before concluding anything is wrong:
       cmp -l old.dylib new.dylib | wc -l          # expect about 112
       otool -l <dylib> | grep -A2 LC_UUID
   (dav1d's osx-x64 slice has the same property for a different reason.)

2. dsymutil WARNS ABOUT THREE DUPLICATE OBJECT NAMES, AND THE WARNING IS BENIGN.

       warning: (x86_64) skipping debug map object with duplicate name and
       timestamp: ... libllama.a(llama.cpp.o) / libggml-cpu.a(quants.c.o) /
       libggml-cpu.a(repack.cpp.o)

   Two objects in the same archive share a basename (ggml-cpu/quants.c.o and
   ggml-cpu/arch/x86/quants.c.o, likewise repack.cpp.o; and llama.cpp.o twice
   through the wrapper's whole-archive link). dsymutil de-duplicates by name
   and timestamp and so skips the second of each. The line tables of those
   three objects are therefore absent from the .dSYM; every other object's
   are present (about 794,000 DW_TAG_subprogram entries), and the unstripped
   dylib's full symbol table covers ALL objects, so any crash address still
   resolves to a function name. Nothing to fix; do not silence the warning by
   dropping -g.

   THE .dSYM IS LARGE - 55 MB for osx-x64 - because full DWARF is generated.
   Jeremy's decision (2026-09-15) is to commit the unstripped twins as dav1d
   does; if the size becomes a problem, the .dSYM is the part to reconsider
   (the 5 MB unstripped dylib alone already gives function-level
   symbolication).


================================================================================
TROUBLESHOOTING
================================================================================
"cmake: command not found" after brew install
    Homebrew's bin directory is not on PATH for this shell. On Apple Silicon
    that is /opt/homebrew/bin, on Intel /usr/local/bin.
    eval "$(/opt/homebrew/bin/brew shellenv)"   (or /usr/local/bin/brew)

"minimum macOS is <version>, expected 13.3"
    The dylib was not built with the deployment target. Almost always a stale
    build directory; both scripts remove theirs and pass the target on every
    run, so use the scripts rather than configuring cmake by hand. Do NOT
    "fix" this by relaxing the check - the value it guards is the oldest macOS
    the shipped package will load on.

"error: 'X' is only available on macOS N or newer [-Werror,-Wunguarded-availability-new]"
    The wrapper's floor check fired: the vendored source calls an API newer
    than MACOS_MIN_VERSION without an @available guard. This is the build
    doing its job - see THE MINIMUM macOS VERSION. Raise the floor in
    pins.env (both slices, rebuild both, record it in BUILD-PROVENANCE.txt)
    or guard the call with a patch in ../patches/. Do NOT drop the -Werror:
    that silently turns the call into a weak import that crashes on the
    floor macOS, which is exactly what the 11.0 builds did.

"no code signature"
    The explicit codesign call failed or was removed. It cannot be left to the
    linker: the linker ad-hoc signs arm64 output only, and stripping plus
    install_name_tool invalidate whatever signature exists. Sign last.

"smoke test NOT RUN" on osx-x64
    Cross route without Rosetta 2. softwareupdate --install-rosetta, then
    re-run. This is reported as a failure on purpose.

"unexpected dynamic dependencies"
    Something linked a library the user would have to install. On the CPU
    slice, seeing Metal/Foundation here means GGML_METAL was not OFF - check
    pins.env LLAMA_CMAKE_OPTIONS_OSX_X64 reached cmake (the script prints the
    full command).

A conformance failure with a real, finite max |diff|
    Take it seriously: this build computes differently from the reference. Do
    not raise the tolerance or edit EXPECTED.txt. See ../test-vectors/README.txt.

A conformance failure reporting NaN logits on the Metal pass (osx-arm64)
    ggml-metal produced garbage on this GPU. On Apple Silicon that would be a
    real finding - upstream supports every Apple GPU - so first confirm the
    CPU pass succeeded, then look at the ggml_metal_device_init lines in the
    log for what the backend thought it had.

The osx-x64 sha256 does not match ../BUILD-PROVENANCE.txt
    Expected, and not a problem, if the ONLY difference is the LC_UUID and the
    signature. See item 1 of WHAT HAS AND HAS NOT BEEN VERIFIED.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ../output/<rid>/BUILD-INFO.txt and satisfy yourself it is the build
     you think it is: llama.cpp commit, toolchain, deployment target, the
     conformance lines (BOTH lines for osx-arm64).

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir -p ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native
       cp ../output/<rid>/libcodebrix_llama.dylib \
          ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/
       cp ../output/<rid>/LICENSE-LlamaCpp.txt \
          ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/

     <rid> is osx-arm64 or osx-x64. The licence copy is not optional: the MIT
     licence requires its notice to accompany copies, and this is how it
     travels. Keep the name libcodebrix_llama.dylib - unversioned.
     LibraryImport("codebrix_llama") probes exactly that.

  3. Do NOT copy the .dSYM or unstripped/ into the package. They do have a
     home: copy ../output/<rid>/unstripped/libcodebrix_llama.dylib and the
     whole ../output/<rid>/libcodebrix_llama.dylib.dSYM/ bundle into
     ../unstripped/<rid>/ and regenerate ../unstripped/SHA256SUMS, in the SAME
     commit that adopts the binary. ../unstripped/README.txt has the rule and
     the LC_UUID check that proves the stored copy is the build that was
     adopted - which matters because the build is not UUID-reproducible.

  4. Record the build in ../BUILD-PROVENANCE.txt, copying the values straight
     out of BUILD-INFO.txt.

  5. Run `codesign -v` on the copied file. Copying preserved the signature on
     2026-09-15, but some transports do not.

  6. Run the managed test suite before publishing.

WHAT WAS ADOPTED ON 2026-09-15
--------------------------------------------------------------------------------
  src/CodeBrix.Ollama.ModelRunner/runtimes/osx-x64/native/libcodebrix_llama.dylib
      sha256 b06df3a7392388dbe4b817175a776cc0438bc3844ec0943e4a365897156ed982
      4,221,552 bytes, LC_UUID 5830F46C-EFA7-3F60-840C-45833A3F0831
      (a rebuild legitimately produces a different sha256 and UUID - item 1
       above; the stored unstripped twin and dSYM carry THIS UUID)
  with llama.cpp's LICENSE beside it as LICENSE-LlamaCpp.txt; signature
  re-verified after the copy. Floor 11.0 - REBUILD REQUIRED (see STATUS).

  src/CodeBrix.Ollama.ModelRunner/runtimes/osx-arm64/native/libcodebrix_llama.dylib
      sha256 65339b09a95f795b075b6e9ea61d2b38fc82b9753a4efd17863e5974c8ac47c4
      4,307,696 bytes, LC_UUID 88DBABCC-377F-3BBB-A943-F3E67AD56808, floor
      13.3 (same UUID caveat; the stored unstripped twin and dSYM carry it)
  with LICENSE-LlamaCpp.txt beside it; signature re-verified after the copy.

  Still outstanding: the osx-x64 REBUILD at the 13.3 floor (Intel Mac mini,
  or the cross route on the Apple Silicon Mac), and every Windows and Linux
  RID.


================================================================================
FILES
================================================================================
  README.txt                this document
  build-common.sh           shared machinery: pins, prerequisites, the gate,
                            collection
  build-osx-x64.sh          osx-x64, native on Intel or cross on Apple Silicon
  build-osx-arm64.sh        osx-arm64, native on Apple Silicon
  ../linux/pins.env         the pins - shared by all three platforms, so there
                            is exactly one file to edit
  ../wrapper/               the CMake project that produces the single library
  ../smoke-test.c           the dlopen verification program
  ../test-vectors/          the conformance model, its reference, and the tools
  ../output/                build results (git-ignored)
================================================================================
