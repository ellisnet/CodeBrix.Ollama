# =============================================================================================
# build-win-x64.ps1 - build codebrix_llama.dll for the win-x64 runtime identifier
# =============================================================================================
#
#   NEVER YET RUN. Written on the Intel Mac mini on 2026-09-15. Expect to fix something on the
#   first real run; fix it IN THE SCRIPT and commit that. Then rewrite this header and
#   README.txt's status block with what the run established.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd llama-native-tools\windows
#     .\build-win-x64.ps1
#
# Built with MSVC (cl) on an x64 Windows machine. Output: ..\output\win-x64\
# It installs nothing. Anything missing is named, with the command that installs it, and the
# script stops.
# =============================================================================================

[CmdletBinding()]
param(
    # Where cmake builds. Kept out of the repository so the vendored source and the output tree
    # stay clean.
    [string] $BuildRoot = (Join-Path $env:TEMP 'codebrix-llama-build-win-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

$toolsDir    = Split-Path -Parent $PSScriptRoot          # llama-native-tools\
$rid         = 'win-x64'
$pins        = Read-Pins (Join-Path $toolsDir 'linux\pins.env')
$sourceDir   = Join-Path $toolsDir $pins['LLAMA_DIR']
$wrapperDir  = Join-Path $toolsDir 'wrapper'
$patchDir    = Join-Path $toolsDir 'patches'
$scratchDir  = Join-Path $env:TEMP 'codebrix-llama-src-win-x64'
$outDir      = Join-Path $toolsDir "output\$rid"
$modelFile   = Join-Path $toolsDir $pins['TEST_VECTOR_MODEL']
$expected    = Join-Path $toolsDir $pins['TEST_VECTOR_EXPECTED']
$libName     = "$($pins['LIBRARY_BASENAME']).dll"
$licenseName = 'LICENSE-LlamaCpp.txt'
$startedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
$stopwatch   = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host '=============================================================================='
Write-Host " codebrix_llama (llama.cpp $($pins['LLAMA_TAG']), $($pins['LLAMA_COMMIT'])) - $rid"
Write-Host '=============================================================================='
Write-Host "  started  : $startedAt"
Write-Host "  host     : $env:PROCESSOR_ARCHITECTURE, $((Get-CimInstance Win32_OperatingSystem).Caption)"
Write-Host "  source   : $sourceDir  (vendored - nothing is downloaded)"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 1. Prerequisites, all of them, before anything is compiled.
# ---------------------------------------------------------------------------------------------
Write-Host '--- prerequisites ---'
$vsPath    = Find-VisualStudio
$vcvarsall = Get-VcVarsAllPath $vsPath
Write-Host "  Visual Studio: $vsPath"

Import-DeveloperEnvironment -VcVarsAll $vcvarsall -ArchArgument 'x64'

$clPath    = Assert-OnPath 'cl'    'Install the Visual Studio 2022 Build Tools with the "Desktop development with C++" workload.'
$cmakePath = Assert-OnPath 'cmake' "Install it: winget install Kitware.CMake  (or pip install cmake==$($pins['CMAKE_VERSION']))"
$ninjaPath = Assert-OnPath 'ninja' "Install it: pip install ninja==$($pins['NINJA_VERSION'])  (or winget install Ninja-build.Ninja)"
$dumpbin   = Assert-OnPath 'dumpbin' 'dumpbin ships with the C++ toolset; it should be on PATH inside the developer environment.'

Write-Host "  cl      : $clPath"
Write-Host "  cmake   : $((& cmake --version | Select-Object -First 1)) (pinned $($pins['CMAKE_VERSION']))  [$cmakePath]"
Write-Host "  ninja   : $((& ninja --version)) (pinned $($pins['NINJA_VERSION']))  [$ninjaPath]"
Write-Host "  dumpbin : $dumpbin"

if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'CMakeLists.txt'))) { throw "The vendored llama.cpp source is missing from $sourceDir. Restore it from git." }
if (-not (Test-Path -LiteralPath $modelFile)) { throw "The conformance model $modelFile is missing. The gate cannot run without it." }
if (-not (Test-Path -LiteralPath $expected))  { throw "The expected-logits file $expected is missing. See test-vectors\README.txt." }
$modelSha = Get-Sha256 $modelFile
if ($modelSha -ne $pins['TEST_VECTOR_MODEL_SHA256'].ToLowerInvariant()) {
    throw "$modelFile has sha256 $modelSha but pins.env says $($pins['TEST_VECTOR_MODEL_SHA256']). The committed conformance model has been altered. Restore it from git."
}
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 2. Source + build
# ---------------------------------------------------------------------------------------------
Write-Host '--- source ---'
$patchesApplied = Copy-SourceToScratch -SourceDir $sourceDir -ScratchDir $scratchDir -PatchDir $patchDir
Write-Host "  patches applied: $patchesApplied"
Write-Host ''

Write-Host '--- building ---'
$buildInfo = "llama.cpp $($pins['LLAMA_TAG']) ($($pins['LLAMA_COMMIT'])) | ggml $($pins['GGML_VERSION']) | $rid | built $startedAt on $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE) | $((& cl 2>&1 | Select-Object -First 1))"
Invoke-WrapperBuild -WrapperDir $wrapperDir -ScratchDir $scratchDir -BuildDir $BuildRoot -Rid $rid -BuildInfo $buildInfo `
                    -CmakeOptions $pins['LLAMA_CMAKE_OPTIONS'] -ArchOptions $pins['LLAMA_CMAKE_OPTIONS_X64']
Copy-Item -LiteralPath (Join-Path $scratchDir 'include\llama.h') -Destination (Join-Path $BuildRoot 'llama-source-header.h') -Force
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 3. Collect. The wrapper names the DLL codebrix_llama.dll (no lib prefix, no version suffix),
#    exactly the name LibraryImport("codebrix_llama") probes for. The .pdb is the Windows
#    equivalent of an unstripped copy; it is kept beside the DLL here and NOT shipped.
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
    Write-Host "  [warn] no .pdb was produced - the wrapper asks for /Zi and /DEBUG in Release; check the build log."
}

Copy-Item -LiteralPath (Join-Path $sourceDir 'LICENSE') -Destination (Join-Path $outDir $licenseName)
Write-Host "  $licenseName : llama.cpp's LICENSE (MIT) copied beside the binary"
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 4. The gate
# ---------------------------------------------------------------------------------------------
Write-Host '--- verifying ---'
$dllPath = Join-Path $outDir $libName
$summary = Test-CodebrixLlamaBinary -DllPath $dllPath -ExpectedMachine 'x64' -Rid $rid -BuildDir $BuildRoot `
                                    -ModelFile $modelFile -ModelSha256 $pins['TEST_VECTOR_MODEL_SHA256'] `
                                    -ExpectedFile $expected -Tolerance $pins['CONFORMANCE_TOLERANCE'] `
                                    -CanRunTargetBinaries $true

if ($script:GateFailures.Count -gt 0) {
    Write-Host ''
    Write-Host "VERIFICATION FAILED for $rid. $outDir is left for inspection, but this build must"
    Write-Host 'not be adopted into the package.'
    exit 1
}
Write-Host ''

# ---------------------------------------------------------------------------------------------
# 5. Record
# ---------------------------------------------------------------------------------------------
$sha = Get-Sha256 $dllPath
$stopwatch.Stop()
$pdbPath = Join-Path $outDir "$($pins['LIBRARY_BASENAME']).pdb"
$debugSymbols = if (Test-Path -LiteralPath $pdbPath) { "$($pins['LIBRARY_BASENAME']).pdb beside it (not shipped)" } else { 'none produced' }

$buildInfoText = @"
codebrix_llama native library - build information
==============================================================================
RID              : $rid
Built            : $startedAt
Build duration   : $([int]$stopwatch.Elapsed.TotalSeconds)s
Built by         : llama-native-tools\windows\build-win-x64.ps1

Build machine
------------------------------------------------------------------------------
OS               : $((Get-CimInstance Win32_OperatingSystem).Caption) ($env:PROCESSOR_ARCHITECTURE)
Visual Studio    : $vsPath
cl               : $((& cl 2>&1 | Select-Object -First 1))
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
arch options     : $($pins['LLAMA_CMAKE_OPTIONS_X64'])
CPU baseline     : x86-64 with AVX2, FMA, F16C, BMI2 (no AVX-512)
GPU backend      : none (CPU-only slice)
CRT              : static (MultiThreaded) - no Visual C++ Redistributable is required on the user's machine
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

# Staging: a compressed copy for moving the binary to whichever machine assembles the package.
# .zip rather than .xz because Compress-Archive is in the box on Windows and xz is not.
$stagingDir = Join-Path $toolsDir "output\staging\$rid"
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Compress-Archive -Path $dllPath -DestinationPath (Join-Path $stagingDir "$libName.zip") -Force

Write-Host '--- done ---'
Write-Host "  $dllPath"
Write-Host "  sha256 $sha"
Write-Host "  staged $stagingDir\$libName.zip"
Write-Host "  $([int]$stopwatch.Elapsed.TotalSeconds)s"
Write-Host ''
Write-Host 'To adopt this binary into the package, follow ADOPTING A BUILT BINARY in README.txt.'
