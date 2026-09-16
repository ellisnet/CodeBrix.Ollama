================================================================================
llama-native-tools/windows - building codebrix_llama.dll for win-x64 and win-arm64
================================================================================

>>> STATUS 2026-09-15: win-x64 BUILT, FULL GATE PASSED, ADOPTED - on the
    Windows 11 x64 machine (i7-12850HX, Visual Studio Professional 2026 18.10,
    MSVC 14.51), third run of the day, 44 s. win-arm64 CROSS-BUILT on the same
    machine (-Route CrossFromX64, clang-cl 22.1.3): the three static checks
    passed, the three executing checks are UNRUN - and Jeremy ADOPTED IT
    ANYWAY, overruling the gate rule, with a native rebuild on ARM64 as the
    plan if it misbehaves (BUILD-PROVENANCE.txt says so in capitals). The DLL
    has never been executed. output\staging\win-arm64\win-arm64-gate.zip is
    still waiting for the ARM64 machine - or, better, run .\build-win-arm64.ps1
    natively there and replace the cross build with a fully gated one. The
    scripts were written on the Intel Mac mini on 2026-09-15 and, as
    predicted, the first real runs found things to fix - five, all fixed in
    the scripts and the wrapper and recorded in ..\BUILD-PROVENANCE.txt: the
    export parser (dumpbin's "name = name" format with a .pdb present), the
    cl version banner (stderr, not stdout), the EXPORT SURFACE (22 C++-mangled
    internal functions leaked through dllexport; the wrapper now generates a
    .def from the archives instead - see THE BUILD below), the clang-cl host
    directory on an x64 host, and the allowed-dependents list, trimmed to
    what is actually imported. <<<

WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to build the two Windows native libraries this package ships,
from the llama.cpp subset vendored in ..\llama.cpp\ and our wrapper project in
..\wrapper\. Nothing is downloaded: not the source, not the conformance model,
not the expected logits. The only things that come from outside are the tools
you install on the build machine, and every one of them is listed below with
the command that installs it.

  win-x64     built with MSVC (cl) on an x64 Windows machine
  win-arm64   built with clang-cl on an ARM64 Windows machine (preferred), or
              cross-compiled from an x64 machine (see the two routes below)

Jeremy has both an x64 and an ARM64 Windows 11 machine, so the plan is one
native build on each.


================================================================================
PREREQUISITES
================================================================================
  1. Visual Studio 2022 or newer, or the standalone Build Tools, with:

       - Workload:  "Desktop development with C++"
       - Component: "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools"
                    (win-arm64 only: the ARM64 linker and Windows SDK
                    libraries come from it even though the compiler is
                    clang-cl; on newer VS the equivalent component works - the
                    scripts check the filesystem for an arm64 cl.exe, not a
                    version number)
       - Component: "C++ Clang tools for Windows"
                    (win-arm64 only - see WHY clang-cl below)

     Install through the Visual Studio Installer, or:
       winget install Microsoft.VisualStudio.2022.BuildTools

     THE COMPONENTS MUST BE IN THE INSTANCE THE BUILD ACTUALLY USES. The scripts
     select an instance with `vswhere -latest` and then check THAT instance's
     filesystem. If you have several Visual Studios, adding clang to the wrong
     one changes nothing - see TROUBLESHOOTING.

     The scripts locate Visual Studio themselves and set up the compiler
     environment on their own, so you can run them from any PowerShell prompt.

  2. cmake, at or above the version pinned in ..\linux\pins.env (4.4.3):

       winget install Kitware.CMake
       (or: pip install cmake==4.4.3)

     Verify: cmake --version.   The wrapper project needs cmake >= 3.24.
     NOTE: Visual Studio's own "C++ CMake tools" component bundles a cmake
     (4.3.1 in VS 2026 18.10) and a ninja; whichever is first on PATH after
     vcvarsall is what the script uses, and it prints the path and version so
     you can see which. On the x64 machine the pip --user install came first
     and the pinned 4.4.3 was used.

  3. ninja, at or above the pinned version (1.13.2):

       pip install --user ninja==1.13.2
       (or: winget install Ninja-build.Ninja)

     --user avoids needing an elevated prompt when Python lives under
     C:\Program Files. Check that the resulting Scripts directory is on PATH -
     for a per-user install it is %APPDATA%\Python\PythonXXX\Scripts.
     (Visual Studio's C++ CMake tools component also ships a ninja.exe that
     vcvarsall puts on PATH; any ninja works.)

  4. PowerShell 5.1 (in the box) or PowerShell 7+.

  NOT required: Python for anything but pip, Perl, MSYS2, Cygwin, git-bash
  (git itself is needed only if ..\patches\ ever holds a patch).


================================================================================
USAGE
================================================================================
    cd llama-native-tools\windows
    .\build-win-x64.ps1

    .\build-win-arm64.ps1                       # on an ARM64 Windows machine
    .\build-win-arm64.ps1 -Route CrossFromX64   # on an x64 Windows machine

  Each script sets up its own developer environment (vcvarsall x64, arm64 or
  x64_arm64), so no special command prompt is needed.

  EXIT CODES. 0 means every check ran and passed. Non-zero means it did not -
  and for  -Route CrossFromX64  a non-zero exit is the NORMAL, EXPECTED result,
  because three of the checks cannot run on an x64 host. Read the summary the
  script prints at the end rather than the exit code alone.

  Timings (2026-09-15, i7-12850HX, 16 cores): win-x64 44 s, win-arm64 cross
  34 s - both cold, the script deletes the build tree every run.

  Output, git-ignored:
    ..\output\<rid>\codebrix_llama.dll     the file the package ships
    ..\output\<rid>\codebrix_llama.pdb     debug symbols - the Windows
                                           equivalent of the unstripped copy.
                                           NOT shipped; goes to ..\unstripped\
    ..\output\<rid>\LICENSE-LlamaCpp.txt   llama.cpp's LICENSE, verbatim
    ..\output\<rid>\BUILD-INFO.txt         toolchain, pins, size, sha256, conformance
    ..\output\<rid>\SHA256SUMS.txt         the DLL's hash on its own line
    ..\output\staging\<rid>\codebrix_llama.dll.zip
                                           compressed copy for moving to whichever
                                           machine assembles the package (.zip
                                           because Compress-Archive is in the box)
    ..\output\staging\win-arm64\win-arm64-gate.zip
                                           CROSS ROUTE ONLY - the DLL plus the
                                           three ARM64 gate tools, to finish the
                                           gate on ARM64 hardware


================================================================================
THE BUILD, IN ONE PARAGRAPH
================================================================================
The script copies ..\llama.cpp to %TEMP%, applies any patches from ..\patches
(none), and configures ..\wrapper\CMakeLists.txt against that copy with the
options from pins.env. The wrapper builds llama.cpp and ggml as static
libraries and links them with /WHOLEARCHIVE into ONE codebrix_llama.dll, with
the STATIC CRT (MultiThreaded), so no Visual C++ Redistributable is needed on a
user's machine. The exported surface comes from a module-definition (.def)
file that the wrapper GENERATES after the static archives are built: it reads
their symbol tables with dumpbin and lists every C-linkage llama_* / ggml_* /
gguf_* symbol they define (..\wrapper\exports-windows.cmake) - the same rule
the version script applies on Linux and the symbol list on macOS. Nothing in
the vendored code is marked dllexport; the first win-x64 run showed that the
LLAMA_API / GGML_API macros also sit on internal C++ functions, which then
leaked as mangled exports. It also builds the three gate tools, static CRT as
well. See ..\wrapper\CMakeLists.txt for the reasoning behind every choice.


================================================================================
THE TWO ARM64 ROUTES, AND WHY clang-cl
================================================================================
ggml's CPU backend refuses MSVC on ARM outright - its CMake says "MSVC is not
supported for ARM, use clang" - because the NEON, dotprod and fp16 intrinsics
it relies on are not all available under cl. clang-cl arrives as a Visual
Studio component, not as another download, so nothing comes from outside.

  -Route Native (default) - run on an ARM64 Windows machine.
      Preferred, because the gate can RUN what it built: the smoke test, the
      model regeneration and the conformance check all execute ARM64 code.
      Jeremy has ARM64 Windows hardware; this is the plan.

  -Route CrossFromX64 - run on an x64 Windows machine.
      Uses vcvarsall x64_arm64 plus -DCMAKE_SYSTEM_NAME=Windows,
      -DCMAKE_SYSTEM_PROCESSOR=ARM64 and the aarch64-pc-windows-msvc compiler
      target. It produces a DLL and runs the static checks, but it CANNOT run
      the three executing checks. The script reports them as FAILURES rather
      than skipping them quietly, exits non-zero, still writes BUILD-INFO.txt
      with "Gate status: INCOMPLETE" at the top, and stages the ARM64 DLL plus
      the ARM64 gate tools so the gate can be finished elsewhere.

  The cross route HAS run (2026-09-15, on the x64 machine): it builds, the
  static checks pass, and it exits 1 with the gate incomplete, exactly as
  designed. cmake did not have meson's "aarch64" spelling trap; the one thing
  that needed adjusting was which clang-cl to run - Visual Studio ships one per
  HOST architecture, and the ARM64-hosted copy cannot start on x64. The native
  route has NOT yet run; it is the one that produces an adoptable binary.


================================================================================
THE VERIFICATION GATE
================================================================================
The same gate as the Linux and macOS builds, expressed with the tools Windows
has. A build that fails any check exits non-zero and must not be adopted.

  1. Machine type - dumpbin /headers must report x64 or ARM64, matching the RID.

  2. Required exports - dumpbin /exports must list EVERY LLAMA_API function
     declared in the include\llama.h the library was built from (extracted from
     the header at gate time, so the list cannot drift), plus the ggml device
     enumeration and gguf metadata functions and the two codebrix_llama_*
     identity functions. (With the .pdb beside the DLL, dumpbin prints every
     export as "name = name (undecorated)"; the parser takes the first token
     after the RVA, and parsing zero names is itself a failure.)

  3. Export surface - every exported name must start with llama_, ggml_, gguf_
     or codebrix_llama_. A C++-mangled name starts with '?' and fails this.
     The generated .def is what makes it hold; the gate re-checks the finished
     file rather than trusting the generator.

  4. Dependents - dumpbin /dependents may list only KERNEL32.dll and
     ADVAPI32.dll - what both real builds (MSVC x64 and clang-cl ARM64)
     actually import; the list in build-common.ps1 was trimmed to exactly
     that on 2026-09-15. NOTHING from the Visual C++ runtime: VCRUNTIME140.dll,
     MSVCP140.dll or any api-ms-win-crt-*.dll appearing there means the static
     CRT did not take, and the package would demand a Visual C++
     Redistributable on every user's machine. Another in-box DLL appearing
     means the vendored code started importing something new; add it to the
     list only with a note in BUILD-PROVENANCE.txt saying what needs it.

  5. LoadLibrary smoke test - ..\smoke-test.c, built by the wrapper as
     smoke-test.exe, loads the STAGED DLL the way .NET does, resolves 75 entry
     points, checks codebrix_llama_rid() equals the RID being built,
     initialises the backend and enumerates devices.

  6. Model regeneration - generate-test-model.exe must reproduce the committed
     conformance model BYTE FOR BYTE (sha256 in pins.env).

  7. Conformance - conformance-check.exe runs the committed model and every
     logit must match ..\test-vectors\EXPECTED.txt within the tolerance, with
     the greedy argmax matching exactly and no NaN anywhere.

  THE PATH TRAP. The gate tools live in the build directory beside the
  build-tree DLL. Before running them the script copies the STAGED DLL - the
  exact file the package ships - over that one, so the executable's own
  directory (first in Windows' DLL search order) supplies it and nothing on
  PATH can. That is the fix dav1d's first Windows run needed, applied in
  advance: on that machine a GStreamer dav1d.dll on PATH was loaded instead of
  the one under test and every decode came back empty. A stray llama.dll from
  LLamaSharp or Ollama on PATH is the same hazard here - one more reason the
  library is named codebrix_llama.dll and not llama.dll.

  There is no glibc-floor equivalent on Windows: the Windows ABI is stable
  across versions and the static CRT removes the redistributable question,
  which is what check 4 covers instead.


================================================================================
FINISHING A CROSS-BUILT win-arm64 ON ARM64 HARDWARE
================================================================================
A cross-built win-arm64 has passed the three static checks. The three executing
checks must be run on an ARM64 Windows machine before the binary is shipped.

  A. VERIFY THE ACTUAL ARTEFACT (what the cross route is designed for)

     Copy ..\output\staging\win-arm64\win-arm64-gate.zip to the ARM64 machine
     and unpack it into one folder; it holds the DLL and the three ARM64 gate
     tools built from the same source. You also need this repository, for the
     conformance assets. Then, in that folder:

       .\smoke-test.exe .\codebrix_llama.dll win-arm64
       .\generate-test-model.exe regen.gguf
       certutil -hashfile regen.gguf SHA256      # must equal pins.env TEST_VECTOR_MODEL_SHA256
       .\conformance-check.exe <repo>\llama-native-tools\test-vectors\codebrix-conformance-tiny.gguf ^
           --check <repo>\llama-native-tools\test-vectors\EXPECTED.txt --tolerance 0.001 --gpu-layers 0

     All three must pass. Then record the result in ..\BUILD-PROVENANCE.txt and
     replace the "Gate status" line in output\win-arm64\BUILD-INFO.txt.

  B. REBUILD NATIVELY (the stronger check, if the machine has the toolchain)

       .\build-win-arm64.ps1

     This runs the whole gate end to end and exits 0 if everything passes. If
     you do this and it passes, prefer the natively built binary and retire the
     cross-built one - a fully gated build beats a partially gated one.


================================================================================
TROUBLESHOOTING
================================================================================
"vswhere.exe was not found"
    No Visual Studio 2017-or-newer installer is present. See PREREQUISITES 1.

"The ARM64 C++ build tools are not installed in THIS Visual Studio instance"
or "clang-cl was not found in this Visual Studio instance"
    Exactly what they say - and note the word THIS. The check reads the
    filesystem of the selected installation on purpose; `vswhere -requires`
    searches EVERY instance and would report a component present because a
    different instance has it. To see which instance will be used:
        & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

cmake reports it cannot find a C compiler
    The developer environment did not import. Check that vcvarsall.bat exists
    at the path the script printed, and try the equivalent command prompt by
    hand ("x64 Native Tools Command Prompt") to see the real error.

dumpbin /dependents lists VCRUNTIME140.dll or api-ms-win-crt-*.dll
    The static CRT setting did not take. The wrapper sets MSVC_RUNTIME_LIBRARY
    on every target it knows about; if a vendored target was missed, add it to
    the loop in ..\wrapper\CMakeLists.txt. Delete the build directory and re-run.

The DLL exports nothing, or the gate reports hundreds of missing exports
    Either the .def was not generated or the gate could not read dumpbin's
    output. Look at <build dir>\codebrix_llama-exports.def: it should list
    over a thousand names ("1147 functions, 5 data symbols" on the first
    win-x64 build). If it is short or missing, ..\wrapper\exports-windows.cmake
    did not understand `dumpbin /symbols` - run it on one of the .lib files
    by hand and compare the line format with the patterns in that script. If
    the .def is fine, the gate's parser in build-common.ps1 is what to check
    (run `dumpbin /exports` on the DLL by hand).

"error LNK2001: unresolved external symbol" naming a llama_/ggml_/gguf_ symbol
    The generated .def lists something the archives declare but do not define.
    exports-windows.cmake only takes SECTnn (defined) symbols and non-zero-size
    UNDEF (COMMON) ones; if upstream starts emitting symbols some other way,
    that filter is where to look.

Exports with '?' in them (C++-mangled names) fail the surface check
    Something is marking C++ functions dllexport again. The wrapper must NOT
    define GGML_SHARED / GGML_BUILD / LLAMA_SHARED / LLAMA_BUILD (upstream puts
    LLAMA_API / GGML_API on internal C++ functions too - src\llama-ext.h,
    ggml\src\ggml-impl.h); the .def is the only export mechanism.

"Program 'clang-cl.exe' failed to run ... not a valid application for this OS
platform" (cross route)
    The ARM64-hosted clang-cl was chosen on an x64 machine. Get-ClangClPath in
    build-common.ps1 picks the host directory from PROCESSOR_ARCHITECTURE; if
    that is wrong on some machine, that is the function to fix.

"file machine type arm64 conflicts with ..." (cross route)
    cmake configured for the host rather than the target. Check that the
    cross route's -DCMAKE_SYSTEM_NAME / -DCMAKE_SYSTEM_PROCESSOR / compiler
    target arguments reached cmake (the script prints the full command).

Conformance produces an EMPTY result, or the checker exits with 0xC0000139
    Another codebrix_llama.dll or a missing dependency. This is THE PATH TRAP
    above; the gate copies the staged DLL beside the tools to prevent it. If
    you see it anyway, something removed that copy. 0xC0000135
    (STATUS_DLL_NOT_FOUND) is the same class of problem.

The sha256 does not match BUILD-PROVENANCE.txt
    Expected on Windows: link.exe stamps a timestamp into the image (dav1d saw
    exactly four bytes differ between two builds of identical source). Check
    the size and the gate result instead. /Brepro would fix it and is
    deliberately not passed.

A conformance failure with a real, finite max |diff|
    Take it seriously: this build computes differently from the reference. Do
    not raise the tolerance or edit EXPECTED.txt to make it pass. See
    ..\test-vectors\README.txt.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Read ..\output\<rid>\BUILD-INFO.txt and satisfy yourself it is the build
     you think it is: llama.cpp commit, toolchain, the conformance line. If its
     "Gate status" says INCOMPLETE, finish the gate first (above).

  2. Copy the library and its licence into the package's runtimes tree:

       mkdir ..\..\src\CodeBrix.Ollama.ModelRunner\runtimes\<rid>\native
       copy ..\output\<rid>\codebrix_llama.dll     ..\..\src\CodeBrix.Ollama.ModelRunner\runtimes\<rid>\native\
       copy ..\output\<rid>\LICENSE-LlamaCpp.txt   ..\..\src\CodeBrix.Ollama.ModelRunner\runtimes\<rid>\native\

     <rid> is win-x64 or win-arm64. The licence copy is not optional: the MIT
     licence requires its notice to accompany copies, and this is how it
     travels. Keep the name codebrix_llama.dll - LibraryImport("codebrix_llama")
     probes exactly that.

  3. Do NOT copy the .pdb into the package. It does have a home: copy
     ..\output\<rid>\codebrix_llama.pdb to ..\unstripped\<rid>\ and extend
     ..\unstripped\SHA256SUMS, in the SAME commit that adopts the binary.

  4. Record the build in ..\BUILD-PROVENANCE.txt, copying the values straight
     out of BUILD-INFO.txt.

  5. Run the managed test suite before publishing.


================================================================================
FILES
================================================================================
  README.txt                 this document
  build-common.ps1           shared machinery: pins, VS discovery, the gate
  build-win-x64.ps1          win-x64
  build-win-arm64.ps1        win-arm64, native or cross
  ..\linux\pins.env          the pins - shared by all three platforms, so there
                             is exactly one file to edit. It lives in the linux
                             folder because that is where the container build
                             sources it as a shell script; this platform parses
                             the same file.
  ..\wrapper\                the CMake project that produces the single library;
                             exports-windows.cmake in it generates the .def
  ..\smoke-test.c            the load-and-run verification program, shared by
                             all three platforms
  ..\test-vectors\           the conformance model, its reference, and the tools
  ..\output\                 build results (git-ignored)
================================================================================
