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

  osx-arm64   NEVER YET RUN. build-osx-arm64.sh refuses to run on an Intel
              Mac, so nothing it does differently - the arm64 baseline flag,
              Metal ON, the Metal conformance pass, the arm64 export check -
              has executed. Expect to fix something on the first real run; fix
              it IN THE SCRIPT and commit that, then rewrite this block.


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
THE MINIMUM macOS VERSION, AND WHY IT IS 11.0
================================================================================
A Mach-O binary records the oldest macOS it will run on. With no explicit
deployment target, clang stamps in the version of the machine doing the
building - so a dylib built on a current Mac is refused by dyld on every older
one. The only fix is to state the floor explicitly.

Both scripts pass CMAKE_OSX_DEPLOYMENT_TARGET=11.0 (and export
MACOSX_DEPLOYMENT_TARGET), and the gate then CHECKS the built file really
carries it (otool -l, LC_BUILD_VERSION minos) rather than merely reporting it.

Why 11.0 (Big Sur): it is the oldest macOS that exists for Apple Silicon, so
the arm64 slice cannot sensibly go lower; using the same number for x86_64
gives the package ONE floor; and the .NET runtime that loads this library has
a floor of its own at least this high. ggml-metal guards its macOS-15-only
features (residency sets, the tensor API) with @available at run time, so the
arm64 slice compiles for 11.0 and simply does not use them on older systems.
VERIFIED for x86_64 on 2026-09-15 (minos 11.0 in the shipped file); for arm64
this is still an assumption until the first run.


================================================================================
THE VERIFICATION GATE
================================================================================
A build that fails any check exits non-zero and must not be adopted. Eleven
checks; the last is arm64-only.

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

  6. Deployment target - CHECKED against 11.0, not merely reported.

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
    4,170,664 bytes (stripped) with exactly the intended dependency list and
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

"minimum macOS is <version>, expected 11.0"
    The dylib was not built with the deployment target. Almost always a stale
    build directory; both scripts remove theirs and pass the target on every
    run, so use the scripts rather than configuring cmake by hand. Do NOT
    "fix" this by relaxing the check - the value it guards is the oldest macOS
    the shipped package will load on. If the arm64 build genuinely cannot
    compile ggml-metal for 11.0, raise MACOS_MIN_VERSION in pins.env for BOTH
    slices and record why in BUILD-PROVENANCE.txt.

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
      4,170,664 bytes, LC_UUID 5830F46C-EFA7-3F60-840C-45833A3F0831
      (a rebuild legitimately produces a different sha256 and UUID - item 1
       above; the stored unstripped twin and dSYM carry THIS UUID)
  with llama.cpp's LICENSE beside it as LICENSE-LlamaCpp.txt; signature
  re-verified after the copy.

  Still outstanding: osx-arm64 (Apple Silicon Mac), and every Windows and
  Linux RID.


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
