================================================================================
llama-native-tools/windows - building codebrix_llama.dll for win-x64 and win-arm64
================================================================================

>>> STATUS: NEVER YET RUN. These scripts were written on the Intel Mac mini on
    2026-09-15, modelled on CodeBrix.VideoPlayback.Dav1d's
    dav1d-native-tools/windows/ (which has run for real on Windows 11 with
    Visual Studio 2026, and whose four first-run fixes are carried over here),
    with meson replaced by our CMake wrapper project. Expect to fix something
    on the first real run - dav1d's Windows scripts needed four fixes. Fix it
    IN THE SCRIPT and commit that; then rewrite this status block and
    BUILD-PROVENANCE.txt with what the run established. <<<

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

  Timings: unknown until the first run. The osx-x64 build on a 6-core Intel
  Mac took about 4 minutes cold; expect the same order of magnitude.

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
user's machine. On Windows the exported surface is what the public API macros
mark dllexport - the wrapper defines GGML_SHARED/GGML_BUILD/LLAMA_SHARED/
LLAMA_BUILD for every translation unit so that the static objects carry the
export directives and nothing is ever dllimport. It also builds the three gate
tools. See ..\wrapper\CMakeLists.txt for the reasoning behind every choice.


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

  Neither route has been run. The cross route in particular carries the lesson
  of dav1d's first Windows run: meson needed the triple spelt "aarch64" and the
  target carried in the CL environment variable. cmake does not have that
  particular trap - CMAKE_SYSTEM_PROCESSOR=ARM64 and CMAKE_<LANG>_COMPILER_TARGET
  are the documented way - but expect something else to need adjusting.


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
     identity functions.

  3. Export surface - every exported name must start with llama_, ggml_, gguf_
     or codebrix_llama_. On Windows this follows from dllexport being applied
     only to the public API macros; the gate re-checks the finished file.

  4. Dependents - dumpbin /dependents may list only in-box system DLLs
     (KERNEL32, ADVAPI32, USER32, bcrypt, ntdll, WS2_32). NOTHING from the
     Visual C++ runtime: VCRUNTIME140.dll, MSVCP140.dll or any
     api-ms-win-crt-*.dll appearing there means the static CRT did not take,
     and the package would demand a Visual C++ Redistributable on every user's
     machine. (The first run will tell which of the allowed in-box DLLs
     llama.cpp actually imports; trim the list in build-common.ps1 to what is
     seen, and record it.)

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
    The dllexport definitions did not reach the vendored objects. The wrapper
    adds GGML_SHARED GGML_BUILD LLAMA_SHARED LLAMA_BUILD with
    add_compile_definitions() BEFORE add_subdirectory(); if upstream's CMake
    later overrides them per target, that is the place to look.

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
  ..\wrapper\                the CMake project that produces the single library
  ..\smoke-test.c            the load-and-run verification program, shared by
                             all three platforms
  ..\test-vectors\           the conformance model, its reference, and the tools
  ..\output\                 build results (git-ignored)
================================================================================
