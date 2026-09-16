================================================================================
llama-native-tools - everything needed to build the codebrix_llama native
                     libraries
================================================================================

THE RULE THIS FOLDER EXISTS FOR
--------------------------------------------------------------------------------
Jeremy, 2026-09-15:

    "Every single file we need, in order to build these native libraries,
    must be housed in that folder. We must not be pulling from external
    sources during the build process. If, a year from now, I need to redo
    these builds, for some reason - and other source code/repos are not
    available to me - I still need to be able to redo the build. The
    exception is the actual tools like cmake and ninja and those kinds of
    tools - I don't want to vendor a bunch of build tools into this folder.
    But all source code and header files, etc must be in the folder - the
    build process cannot pull from other repos, etc."

Everything below follows from that. The folder is modelled, file for file, on
CodeBrix.VideoPlayback.Dav1d's dav1d-native-tools/, which was built to the same
rule; the differences are the ones llama.cpp forces (CMake instead of meson, a
wrapper project that folds six libraries into one, a floating-point conformance
check instead of an md5).


HOW THE RULE IS ENFORCED, NOT JUST STATED
--------------------------------------------------------------------------------
The Linux build runs inside a container started with `--network none`. The first
thing it prints is the proof: the container has no network interface but
loopback. If any step ever tried to fetch a source file, a test asset or a
dependency, it would fail there instead of quietly working on whichever machine
happened to have a connection. The Windows and macOS scripts fetch nothing
either, but Linux is where the rule is mechanically demonstrated on every run.

The one thing in the vendored source that COULD fetch - ggml's optional download
of Arm's KleidiAI kernels - is switched off explicitly in pins.env
(GGML_CPU_KLEIDIAI=OFF), not left to the upstream default. See
llama.cpp/UPSTREAM.txt.

The two things that live outside the folder, by Jeremy's stated exception:
  * the tools installed on each build machine (cmake, ninja, the compilers, a
    container engine) - each platform README lists them with install commands;
  * on Linux, the digest-pinned manylinux container images that supply the
    compiler and the glibc floor (and the cmake/ninja wheels pip installs into
    them). linux/README.txt documents a bare-host route for the day those are
    gone.


FOLDER MAP
--------------------------------------------------------------------------------
  README.txt              this document
  BUILD-PROVENANCE.txt    what was actually built, when, by what, with what
                          hashes - one section per runtime identifier
  smoke-test.c            the load-and-run verification program. One file, no
                          llama.cpp headers, no build system needed; all three
                          platforms run it as part of their gate
  .gitignore              re-includes this folder's contents from the root
                          .gitignore (which ignores names that occur inside the
                          vendored source) and ignores output/. Its first rule
                          is load-bearing - read the comment before editing it

  llama.cpp/              THE VENDORED UPSTREAM SOURCE - a build-sufficient
                          SUBSET of llama.cpp (CMakeLists.txt, LICENSE, cmake/,
                          ggml/, src/, include/; 25 MB), unmodified.
                          UPSTREAM.txt records the URL, tag, commit, dates, the
                          exact copy command, what was deliberately NOT copied
                          and why. Never edited - if a build ever needs a change
                          it goes in patches/
  wrapper/                OUR CMake project: builds the vendored tree as static
                          archives and links them whole-archive into ONE shared
                          library, codebrix_llama, with a restricted export
                          list (exports-linux.map, exports-macos.txt, and on
                          Windows a .def generated from the archives by
                          exports-windows.cmake); adds two identity functions;
                          builds the gate tools. Read its header comment for
                          why one library
  patches/                local changes to the vendored source, applied at build
                          time to a scratch copy. EMPTY as of 2026-09-15
  test-vectors/           the conformance model (synthetic, generated here, NOT
                          third-party content), its reference logits, and the
                          C generator and checker - see its README.txt

  linux/                  linux-x64, linux-arm64, linux-riscv64
                          pins.env (THE pins, for all three platforms),
                          build.sh, container-build.sh, Containerfile.<arch>,
                          README.txt
  windows/                win-x64, win-arm64
                          build-common.ps1, build-win-x64.ps1,
                          build-win-arm64.ps1, README.txt
  macos/                  osx-x64, osx-arm64
                          build-common.sh, build-osx-x64.sh,
                          build-osx-arm64.sh, README.txt

  unstripped/             COMMITTED. The pre-strip twin of every shipped native,
                          one folder per RID (plus the .dSYM on macOS, the .pdb
                          on Windows; the Linux ELFs xz-compressed, because raw
                          they exceed GitHub's per-file limit), with SHA256SUMS
                          and a README.txt carrying the rule for keeping them in
                          step with the binaries in runtimes/<rid>/native/. For
                          crash triage: never shipped, never an input to any
                          build

  output/                 build results (git-ignored except its README.txt).
                          Disposable - the pre-strip copies that are meant to
                          last live in unstripped/, not here


WHICH README TO READ
--------------------------------------------------------------------------------
  Building on Linux    -> linux/README.txt
  Building on Windows  -> windows/README.txt
  Building on a Mac    -> macos/README.txt

Each one lists the tools to install on that machine, with the exact command,
and nothing else is needed.

  >>> STATUS 2026-09-15: ALL SEVEN slices are built and adopted - six with the
      full gate passed, and win-arm64 adopted by Jeremy's decision WITHOUT its
      executing checks (see the end of this block).
      osx-x64 on the Intel Mac mini and osx-arm64 on the Apple Silicon Mac
      mini, full gate passed on both, both at the 13.3 macOS floor. The arm64
      run found that the original floor of 11.0 was never real (a 13.3-only
      Accelerate symbol was weak-imported by BOTH slices); the floor is now
      13.3, the wrapper enforces it at compile time, and osx-x64 was REBUILT
      and re-adopted at 13.3 the same day (BUILD-PROVENANCE.txt). linux-x64,
      linux-arm64 and linux-riscv64 were built the same evening on the x86_64
      LMDE laptop through the manylinux container route (arm64 and riscv64
      under qemu-user emulation), full gate passed on all three; the three
      first-run fixes (wrapper --exclude-libs, the aarch64 probe, the riscv64
      static libstdc++) are in BUILD-PROVENANCE.txt. win-x64 was built on the
      Windows 11 x64 machine the same night (MSVC 14.51, Visual Studio 2026),
      full gate passed on the third run and adopted; the first two runs found
      five things, the largest being that dllexport through the upstream API
      macros leaked 22 C++-mangled internal functions, so on Windows the
      wrapper now GENERATES a .def from the archives (wrapper/
      exports-windows.cmake) - all in BUILD-PROVENANCE.txt. win-arm64 was
      CROSS-BUILT on that x64 machine (clang-cl): static checks passed,
      executing checks UNRUN because x64 cannot run ARM64 code. Jeremy
      overruled the no-adoption-without-a-gate rule and adopted it as is,
      intending a native rebuild on an ARM64 machine if it misbehaves; that
      DLL has never been executed. Finish its gate when an ARM64 Windows
      machine is at hand: run the tools in output/staging/win-arm64/
      win-arm64-gate.zip or, better, windows\build-win-arm64.ps1 natively
      (windows/README.txt), and record the result in BUILD-PROVENANCE.txt. <<<


THE SEVEN RUNTIME IDENTIFIERS
--------------------------------------------------------------------------------
  RID             built by                              shipped file
  --------------  ------------------------------------  --------------------------
  linux-x64       linux/build.sh x64                    libcodebrix_llama.so
  linux-arm64     linux/build.sh arm64                  libcodebrix_llama.so
  linux-riscv64   linux/build.sh riscv64                libcodebrix_llama.so
  win-x64         windows\build-win-x64.ps1             codebrix_llama.dll
  win-arm64       windows\build-win-arm64.ps1           codebrix_llama.dll
  osx-arm64       macos/build-osx-arm64.sh              libcodebrix_llama.dylib
  osx-x64         macos/build-osx-x64.sh                libcodebrix_llama.dylib

  Backends: CPU on every RID; Metal in addition on osx-arm64 only (V1 decision,
  Jeremy, 2026-09-15). CPU floors: AVX2+FMA+F16C+BMI2 on x64 (no pre-AVX2
  support); armv8.2-a+dotprod+fp16 on arm64 (no Raspberry Pi 4); scalar
  rv64gc on riscv64 (no RVV). All in linux/pins.env, with the reasoning.

All seven names are UNVERSIONED and PACKAGE-UNIQUE on purpose. The managed
binding declares LibraryImport("codebrix_llama") and .NET probes for exactly
those file names; and a plain "llama" name would collide with LLamaSharp's
llama.dll or a stock Ollama on a user's PATH. CodeBrix.Audio names its native
codebrix_miniaudio for the same reason.

Every RID folder also gets a LICENSE-LlamaCpp.txt - a verbatim copy of
llama.cpp's LICENSE (MIT). The MIT licence requires its copyright notice and
permission notice to accompany copies of the software; shipping it beside the
binary in runtimes/<rid>/native/ is how this package satisfies that, in
addition to the repository-root THIRD-PARTY-NOTICES.txt. The name is
package-unique (LICENSE-Dav1d.txt, LICENSE-Pdfium.txt and LICENSE-MiniAudio.txt
are the family's precedents) because these files land in a consuming
application's OUTPUT FOLDER, where a file named plainly LICENSE collides with
any other package that ships one there.


THE VENDORED COMMIT
--------------------------------------------------------------------------------
  llama.cpp tag b10221 = commit 815a2a5915f22ce6a760c676389c5dfe8535c08f
  ("vendor : update BoringSSL to 0.20260730.0 (#26353)", 2026-08-01)
  ggml 0.18.0.  MIT.

It is the commit LLamaSharp v0.29.0+34 pins its bindings to, so the managed
binding in CodeBrix.Ollama.ModelRunner, this source and the shipped natives all
describe one C API. llama.cpp/UPSTREAM.txt explains the choice and records how
to verify the snapshot against upstream.


LICENCES AND NOTICES
--------------------------------------------------------------------------------
../THIRD-PARTY-NOTICES.txt, at the root of this repository, inventories every
copyright holder and every licence that appears in the vendored snapshot - the
MIT that covers llama.cpp and ggml themselves, plus the Apache-2.0 and other
permissive files that upstream includes in backends this package never
compiles - with the full text of each licence and the file paths it covers.
Read it before changing what this folder vendors.

The conformance model in test-vectors/ is NOT third-party content and is
deliberately absent from that file: it is random weights from a fixed seed,
generated here by generate-test-model.c, and it belongs to this repository.


A ONE-MINUTE TOUR (on a Mac)
--------------------------------------------------------------------------------
    cd macos
    ./build-osx-x64.sh          # about 4 minutes cold; builds, verifies, stages
    cat ../output/osx-x64/BUILD-INFO.txt
    ls -la ../output/osx-x64/   # libcodebrix_llama.dylib is the shipped file

  On Linux the equivalent is:

    cd linux
    ./build.sh x64              # in a container; timing unknown until run
    cat ../output/linux-x64/BUILD-INFO.txt
    cd ../output && sha256sum -c SHA256SUMS
================================================================================
