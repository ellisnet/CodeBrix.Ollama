# =============================================================================================
# build-win-arm64.ps1 - build codebrix_llama.dll for the win-arm64 runtime identifier
# =============================================================================================
#
#   CROSS ROUTE RUN FOR REAL on 2026-09-15 on the Windows 11 x64 machine: builds, static checks
#   pass, exits 1 with the gate incomplete as designed (..\BUILD-PROVENANCE.txt). Its one fix -
#   choosing the x64-HOSTED clang-cl on an x64 host - is in build-common.ps1. The NATIVE route
#   has not yet run; it is the one that yields an adoptable binary. Expect to fix something on
#   its first run; fix it IN THE SCRIPT and rewrite this header and README.txt's status block.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd llama-native-tools\windows
#     .\build-win-arm64.ps1                       # on an ARM64 Windows machine (preferred)
#     .\build-win-arm64.ps1 -Route CrossFromX64   # on an x64 Windows machine (partial gate)
#
# WHY clang-cl AND NOT cl
#   ggml's CPU backend refuses MSVC on ARM outright ("MSVC is not supported for ARM, use clang"
#   in ggml/src/ggml-cpu/CMakeLists.txt) - the NEON, dotprod and fp16 intrinsics it relies on
#   are not all available under cl. clang-cl arrives as a Visual Studio component ("C++ Clang
#   tools for Windows"), so nothing has to be downloaded from anywhere else. It targets
#   aarch64-pc-windows-msvc, links with the MSVC linker (or lld-link) against the Windows SDK,
#   and produces a normal ARM64 DLL with the static CRT.
#
# THE TWO ROUTES
#   -Route Native (default) - run on an ARM64 Windows machine. Preferred, because the gate can
#       RUN what it built. Jeremy has ARM64 Windows hardware, so this is the plan.
#   -Route CrossFromX64 - run on an x64 Windows machine. Uses vcvarsall x64_arm64 and passes
#       the target triple to cmake. It produces a DLL and runs the static checks, but it CANNOT
#       run the smoke test, the model regeneration or the conformance check. Those are reported
#       as FAILURES and the script exits 1: an unrun check is not a passed check. It still
#       writes BUILD-INFO.txt with the incompleteness stated, and stages the DLL plus the three
#       ARM64 gate tools so the gate can be finished on ARM64 hardware (README.txt).
#
# Output: ..\output\win-arm64\
# =============================================================================================

[CmdletBinding()]
param(
    [ValidateSet('Native', 'CrossFromX64')]
    [string] $Route = 'Native',
    [string] $BuildRoot = (Join-Path $env:TEMP 'codebrix-llama-build-win-arm64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

$toolsDir    = Split-Path -Parent $PSScriptRoot
$rid         = 'win-arm64'
$pins        = Read-Pins (Join-Path $toolsDir 'linux\pins.env')
$sourceDir   = Join-Path $toolsDir $pins['LLAMA_DIR']
$wrapperDir  = Join-Path $toolsDir 'wrapper'
$patchDir    = Join-Path $toolsDir 'patches'
$scratchDir  = Join-Path $env:TEMP 'codebrix-llama-src-win-arm64'
$outDir      = Join-Path $toolsDir "output\$rid"
$modelFile   = Join-Path $toolsDir $pins['TEST_VECTOR_MODEL']
$expected    = Join-Path $toolsDir $pins['TEST_VECTOR_EXPECTED']
$libName     = "$($pins['LIBRARY_BASENAME']).dll"
$licenseName = 'LICENSE-LlamaCpp.txt'
$startedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
$stopwatch   = [System.Diagnostics.Stopwatch]::StartNew()

$hostIsArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64')
if ($Route -eq 'Native' -and -not $hostIsArm64) {
    throw "-Route Native must run on an ARM64 Windows machine; this host is $env:PROCESSOR_ARCHITECTURE. Use -Route CrossFromX64 here, or run on the ARM64 machine."
}
$canRun = $hostIsArm64

Write-Host '=============================================================================='
Write-Host " codebrix_llama (llama.cpp $($pins['LLAMA_TAG']), $($pins['LLAMA_COMMIT'])) - $rid"
Write-Host '=============================================================================='
Write-Host "  started  : $startedAt"
Write-Host "  host     : $env:PROCESSOR_ARCHITECTURE, $((Get-CimInstance Win32_OperatingSystem).Caption)"
Write-Host "  route    : $Route"
Write-Host "  source   : $sourceDir  (vendored - nothing is downloaded)"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 1. Prerequisites
# ---------------------------------------------------------------------------------------------
Write-Host '--- prerequisites ---'
$vsPath    = Find-VisualStudio
$vcvarsall = Get-VcVarsAllPath $vsPath
Write-Host "  Visual Studio: $vsPath"

if (-not (Test-Arm64ToolsPresent $vsPath)) {
    throw @"
The ARM64 C++ build tools are not installed in THIS Visual Studio instance ($vsPath).
Add the component "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools" (or its VS 2026
equivalent) to that instance - see README.txt, PREREQUISITES. The linker and the ARM64
Windows SDK libraries come from it even though the compiler is clang-cl.
"@
}
$clangCl = Get-ClangClPath $vsPath
if (-not $clangCl) {
    throw @"
clang-cl was not found in this Visual Studio instance ($vsPath).
Add the component "C++ Clang tools for Windows" to that instance - see README.txt.
"@
}

$archArgument = if ($Route -eq 'Native') { 'arm64' } else { 'x64_arm64' }
Import-DeveloperEnvironment -VcVarsAll $vcvarsall -ArchArgument $archArgument

$cmakePath = Assert-OnPath 'cmake' "Install it: winget install Kitware.CMake  (or pip install cmake==$($pins['CMAKE_VERSION']))"
$ninjaPath = Assert-OnPath 'ninja' "Install it: pip install ninja==$($pins['NINJA_VERSION'])  (or winget install Ninja-build.Ninja)"
$dumpbin   = Assert-OnPath 'dumpbin' 'dumpbin ships with the C++ toolset; it should be on PATH inside the developer environment.'

Write-Host "  clang-cl: $clangCl"
Write-Host "  cmake   : $((& cmake --version | Select-Object -First 1)) (pinned $($pins['CMAKE_VERSION']))  [$cmakePath]"
Write-Host "  ninja   : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))  [$ninjaPath]"
Write-Host "  dumpbin : $dumpbin"
if ($canRun) { Write-Host '  execute : ARM64 host - the full gate will be applied' }
else { Write-Host '  execute : x64 host - the smoke test, model regeneration and conformance CANNOT run here and will be reported as failures' }

if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'CMakeLists.txt'))) { throw "The vendored llama.cpp source is missing from $sourceDir. Restore it from git." }
if (-not (Test-Path -LiteralPath $modelFile)) { throw "The conformance model $modelFile is missing." }
if (-not (Test-Path -LiteralPath $expected))  { throw "The expected-logits file $expected is missing." }
$modelSha = Get-Sha256 $modelFile
if ($modelSha -ne $pins['TEST_VECTOR_MODEL_SHA256'].ToLowerInvariant()) {
    throw "$modelFile has sha256 $modelSha but pins.env says $($pins['TEST_VECTOR_MODEL_SHA256']). Restore it from git."
}
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 2. Source + build. clang-cl for both routes; the cross route additionally names the target
#    system so cmake configures for ARM64 rather than the host.
# ---------------------------------------------------------------------------------------------
Write-Host '--- source ---'
$patchesApplied = Copy-SourceToScratch -SourceDir $sourceDir -ScratchDir $scratchDir -PatchDir $patchDir
Write-Host "  patches applied: $patchesApplied"
Write-Host ''

Write-Host '--- building ---'
$extra = @("-DCMAKE_C_COMPILER=$clangCl", "-DCMAKE_CXX_COMPILER=$clangCl")
if ($Route -eq 'CrossFromX64') {
    $extra += @('-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_SYSTEM_PROCESSOR=ARM64',
                '-DCMAKE_C_COMPILER_TARGET=aarch64-pc-windows-msvc', '-DCMAKE_CXX_COMPILER_TARGET=aarch64-pc-windows-msvc')
}
$buildInfo = "llama.cpp $($pins['LLAMA_TAG']) ($($pins['LLAMA_COMMIT'])) | ggml $($pins['GGML_VERSION']) | $rid | built $startedAt on $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE, route $Route) | $((& $clangCl --version 2>&1 | Select-Object -First 1))"
Invoke-WrapperBuild -WrapperDir $wrapperDir -ScratchDir $scratchDir -BuildDir $BuildRoot -Rid $rid -BuildInfo $buildInfo `
                    -CmakeOptions $pins['LLAMA_CMAKE_OPTIONS'] -ArchOptions $pins['LLAMA_CMAKE_OPTIONS_ARM64'] -ExtraCmakeArgs $extra
Copy-Item -LiteralPath (Join-Path $scratchDir 'include\llama.h') -Destination (Join-Path $BuildRoot 'llama-source-header.h') -Force
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 3. Collect
# ---------------------------------------------------------------------------------------------
Write-Host '--- collecting ---'
$builtDll = Join-Path $BuildRoot $libName
if (-not (Test-Path -LiteralPath $builtDll)) { throw "No $libName was produced in $BuildRoot." }
foreach ($tool in @('smoke-test.exe', 'generate-test-model.exe', 'conformance-check.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $BuildRoot $tool))) { throw "The gate tool $tool was not built." }
}

if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Copy-Item -LiteralPath $builtDll -Destination (Join-Path $outDir $libName)
$builtPdb = Join-Path $BuildRoot "$($pins['LIBRARY_BASENAME']).pdb"
if (Test-Path -LiteralPath $builtPdb) {
    Copy-Item -LiteralPath $builtPdb -Destination (Join-Path $outDir "$($pins['LIBRARY_BASENAME']).pdb")
    Write-Host "  $($pins['LIBRARY_BASENAME']).pdb kept beside the DLL (crash triage; not shipped)"
}
else {
    Write-Host '  [warn] no .pdb was produced - check the build log.'
}
Copy-Item -LiteralPath (Join-Path $sourceDir 'LICENSE') -Destination (Join-Path $outDir $licenseName)
Write-Host "  $licenseName : llama.cpp's LICENSE (MIT) copied beside the binary"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 4. The gate
# ---------------------------------------------------------------------------------------------
Write-Host '--- verifying ---'
$dllPath = Join-Path $outDir $libName
$summary = Test-CodebrixLlamaBinary -DllPath $dllPath -ExpectedMachine 'ARM64' -Rid $rid -BuildDir $BuildRoot `
                                    -ModelFile $modelFile -ModelSha256 $pins['TEST_VECTOR_MODEL_SHA256'] `
                                    -ExpectedFile $expected -Tolerance $pins['CONFORMANCE_TOLERANCE'] `
                                    -CanRunTargetBinaries $canRun

$gateStatus = if ($script:GateFailures.Count -eq 0) { 'COMPLETE - every check ran and passed' }
              elseif (-not $canRun) { 'INCOMPLETE - cross-built on x64; the smoke test, model regeneration and conformance are UNRUN. Finish on ARM64 hardware (README.txt).' }
              else { 'FAILED' }

# ---------------------------------------------------------------------------------------------
# 5. Record. A cross build records its result even though it exits 1 - the one route that most
#    needs a paper trail is the one whose output travels to another machine to be finished. A
#    build that fails for any OTHER reason records nothing.
# ---------------------------------------------------------------------------------------------
if ($script:GateFailures.Count -gt 0 -and $canRun) {
    Write-Host ''
    Write-Host "VERIFICATION FAILED for $rid. $outDir is left for inspection, but this build must"
    Write-Host 'not be adopted into the package.'
    exit 1
}

$sha = Get-Sha256 $dllPath
$stopwatch.Stop()
$pdbPath = Join-Path $outDir "$($pins['LIBRARY_BASENAME']).pdb"
$debugSymbols = if (Test-Path -LiteralPath $pdbPath) { "$($pins['LIBRARY_BASENAME']).pdb beside it (not shipped)" } else { 'none produced' }

$buildInfoText = @"
codebrix_llama native library - build information
==============================================================================
RID              : $rid
Gate status      : $gateStatus
Built            : $startedAt
Build duration   : $([int]$stopwatch.Elapsed.TotalSeconds)s
Built by         : llama-native-tools\windows\build-win-arm64.ps1  (route: $Route)

Build machine
------------------------------------------------------------------------------
OS               : $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE)
Visual Studio    : $vsPath
clang-cl         : $((& $clangCl --version 2>&1 | Select-Object -First 1))
cmake            : $((& cmake --version | Select-Object -First 1)) (pinned $($pins['CMAKE_VERSION']))
ninja            : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
llama.cpp        : tag $($pins['LLAMA_TAG']), commit $($pins['LLAMA_COMMIT']), ggml $($pins['GGML_VERSION'])
Vendored at      : llama-native-tools\llama.cpp\ (see UPSTREAM.txt)
Patches applied  : $patchesApplied

Configuration
------------------------------------------------------------------------------
cmake options    : $($pins['LLAMA_CMAKE_OPTIONS'])
arch options     : $($pins['LLAMA_CMAKE_OPTIONS_ARM64'])
extra            : $($extra -join ' ')
CPU baseline     : armv8.2-a + dotprod + fp16
GPU backend      : none (CPU-only slice)
CRT              : static (MultiThreaded)
Wrapper          : llama-native-tools\wrapper\CMakeLists.txt (static archives, /WHOLEARCHIVE, one DLL)

Result
------------------------------------------------------------------------------
File             : $libName  (unversioned - LibraryImport("$($pins['LIBRARY_BASENAME'])") probes this name)
Size             : $((Get-Item -LiteralPath $dllPath).Length) bytes
SHA256           : $sha
Debug symbols    : $debugSymbols
Licence beside it: $licenseName (a verbatim copy of llama.cpp's LICENSE, MIT)

Conformance (test-vectors\codebrix-conformance-tiny.gguf vs EXPECTED.txt, tolerance $($pins['CONFORMANCE_TOLERANCE']))
------------------------------------------------------------------------------
$($summary -join "`n")
"@

Set-Content -LiteralPath (Join-Path $outDir 'BUILD-INFO.txt') -Value $buildInfoText -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outDir 'SHA256SUMS.txt') -Value "$sha  $libName" -Encoding UTF8

$stagingDir = Join-Path $toolsDir "output\staging\$rid"
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Compress-Archive -Path $dllPath -DestinationPath (Join-Path $stagingDir "$libName.zip") -Force
if (-not $canRun) {
    # The cross route stages the DLL together with the ARM64 gate tools, so the unrun checks can
    # be completed on ARM64 hardware from one archive.
    $gateFiles = @($dllPath) + (@('smoke-test.exe', 'generate-test-model.exe', 'conformance-check.exe') | ForEach-Object { Join-Path $BuildRoot $_ })
    Compress-Archive -Path $gateFiles -DestinationPath (Join-Path $stagingDir 'win-arm64-gate.zip') -Force
    Write-Host "  staged $stagingDir\win-arm64-gate.zip (dll + the three ARM64 gate tools, to finish the gate on ARM64 hardware)"
}

Write-Host '--- done ---'
Write-Host "  $dllPath"
Write-Host "  sha256 $sha"
Write-Host "  staged $stagingDir\$libName.zip"
Write-Host "  $([int]$stopwatch.Elapsed.TotalSeconds)s"
Write-Host ''

if ($script:GateFailures.Count -gt 0) {
    Write-Host 'GATE INCOMPLETE: this DLL was cross-built and has NOT been executed. Finish the gate on an'
    Write-Host 'ARM64 Windows machine before adopting it - see README.txt, "FINISHING A CROSS-BUILT win-arm64".'
    exit 1
}
Write-Host 'To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt.'
