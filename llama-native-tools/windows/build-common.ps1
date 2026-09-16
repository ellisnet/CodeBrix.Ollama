# =============================================================================================
# build-common.ps1 - shared machinery for the Windows codebrix_llama builds
# =============================================================================================
#
#   NEVER YET RUN. Written on the Intel Mac mini on 2026-09-15, modelled on
#   CodeBrix.VideoPlayback.Dav1d's dav1d-native-tools/windows/build-common.ps1 (which has run
#   for real, and whose four first-run fixes are carried over here), with meson replaced by
#   our CMake wrapper project. Expect to fix something on the first real run; fix it IN THE
#   SCRIPT and commit that. Then rewrite README.txt's status block.
#
# Dot-source this from an architecture script; do not run it directly.
#
#     . "$PSScriptRoot\build-common.ps1"
#
# Everything it needs is in this repository: ..\llama.cpp (vendored source subset), ..\wrapper
# (our CMake project), ..\smoke-test.c, ..\test-vectors\ (conformance model, reference, tools),
# ..\linux\pins.env (the pins, shared by all three platforms - see README.txt for why they live
# in the linux folder).
# =============================================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Pins. One source of truth for every platform: ..\linux\pins.env. Only the keys that are not
# Linux-specific are used here.
# ---------------------------------------------------------------------------------------------
function Read-Pins {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "pins.env was not found at $Path. It is part of the repository - if it is missing after a clone, check the root .gitignore's blanket '*.env' rule (see README.txt, TROUBLESHOOTING)."
    }

    $pins = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trimmed.Substring(0, $eq).Trim()
        $value = $trimmed.Substring($eq + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $pins[$key] = $value
    }
    return $pins
}

# ---------------------------------------------------------------------------------------------
# Visual Studio discovery.
#
# IMPORTANT: component checks read the filesystem of the SELECTED installation, never
# `vswhere -requires`. A machine often has more than one VS instance, and -requires searches
# ALL of them: it will happily report the ARM64 compiler as present because some OTHER instance
# has it. That exact conflation has produced mysterious build failures in this family before.
# ---------------------------------------------------------------------------------------------
function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw @"
vswhere.exe was not found at
    $vswhere
which means no Visual Studio 2017-or-newer installer is present. Install the Visual Studio 2022
Build Tools with the "Desktop development with C++" workload - see README.txt, PREREQUISITES.
"@
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installPath) {
        throw 'No Visual Studio installation with the C++ toolset was found. Install the "Desktop development with C++" workload - see README.txt, PREREQUISITES.'
    }
    return ($installPath | Select-Object -First 1)
}

function Get-VcVarsAllPath {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    $vcvarsall = Join-Path $VsInstallPath 'VC\Auxiliary\Build\vcvarsall.bat'
    if (-not (Test-Path -LiteralPath $vcvarsall)) {
        throw "vcvarsall.bat was not found in $VsInstallPath. The C++ workload is not installed in THIS instance."
    }
    return $vcvarsall
}

# Does the SELECTED installation actually have the ARM64 compiler? Checked on disk.
function Test-Arm64ToolsPresent {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    $msvcRoot = Join-Path $VsInstallPath 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $msvcRoot)) { return $false }
    foreach ($toolset in Get-ChildItem -LiteralPath $msvcRoot -Directory) {
        foreach ($hostDir in @('Hostx64', 'Hostarm64')) {
            $cl = Join-Path $toolset.FullName "bin\$hostDir\arm64\cl.exe"
            if (Test-Path -LiteralPath $cl) { return $true }
        }
    }
    return $false
}

function Get-ClangClPath {
    param([Parameter(Mandatory)][string] $VsInstallPath)

    foreach ($hostDir in @('ARM64', 'x64')) {
        $clangCl = Join-Path $VsInstallPath "VC\Tools\Llvm\$hostDir\bin\clang-cl.exe"
        if (Test-Path -LiteralPath $clangCl) { return $clangCl }
    }
    return $null
}

# ---------------------------------------------------------------------------------------------
# Import a developer environment into THIS PowerShell process.
# $ArchArgument is what vcvarsall takes: x64, arm64, or x64_arm64 (cross from an x64 host).
# ---------------------------------------------------------------------------------------------
function Import-DeveloperEnvironment {
    param(
        [Parameter(Mandatory)][string] $VcVarsAll,
        [Parameter(Mandatory)][string] $ArchArgument
    )

    Write-Host "  developer environment: vcvarsall.bat $ArchArgument"
    $output = & "$env:COMSPEC" /s /c "`"$VcVarsAll`" $ArchArgument >nul 2>&1 && set"
    if ($LASTEXITCODE -ne 0) {
        throw "vcvarsall.bat $ArchArgument failed (exit $LASTEXITCODE). The toolset for that target is probably not installed in this Visual Studio instance."
    }
    foreach ($line in $output) {
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { continue }
        $name = $line.Substring(0, $eq)
        $value = $line.Substring($eq + 1)
        Set-Item -Path "Env:$name" -Value $value
    }
}

function Assert-OnPath {
    param(
        [Parameter(Mandatory)][string] $Tool,
        [Parameter(Mandatory)][string] $InstallHint
    )
    $found = Get-Command $Tool -ErrorAction SilentlyContinue
    if (-not $found) {
        throw @"
$Tool is not on PATH.

$InstallHint

This script does not install anything for you. See README.txt, PREREQUISITES.
"@
    }
    return $found.Source
}

# ---------------------------------------------------------------------------------------------
# Source: copied to scratch and built there, so ..\llama.cpp stays an unmodified snapshot.
# ---------------------------------------------------------------------------------------------
function Copy-SourceToScratch {
    param(
        [Parameter(Mandatory)][string] $SourceDir,
        [Parameter(Mandatory)][string] $ScratchDir,
        [Parameter(Mandatory)][string] $PatchDir
    )

    if (Test-Path -LiteralPath $ScratchDir) { Remove-Item -LiteralPath $ScratchDir -Recurse -Force }
    Copy-Item -LiteralPath $SourceDir -Destination $ScratchDir -Recurse
    Write-Host "  copied $SourceDir -> $ScratchDir"

    $applied = @()
    if (Test-Path -LiteralPath $PatchDir) {
        foreach ($patch in Get-ChildItem -LiteralPath $PatchDir -Filter '*.patch' | Sort-Object Name) {
            $git = Get-Command git -ErrorAction SilentlyContinue
            if (-not $git) {
                throw "patches/$($patch.Name) needs applying and git is not available. Install Git for Windows."
            }
            Write-Host "  applying $($patch.Name)"
            & git -C $ScratchDir apply -p1 $patch.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "patch $($patch.Name) did not apply. Fix it; patches are never applied best-effort."
            }
            $applied += $patch.Name
        }
    }
    if ($applied.Count -eq 0) { return 'none' }
    return ($applied -join ' ')
}

# ---------------------------------------------------------------------------------------------
# Configure + build the wrapper project with cmake + ninja. Every option comes from pins.env.
# ---------------------------------------------------------------------------------------------
function Invoke-WrapperBuild {
    param(
        [Parameter(Mandatory)][string] $WrapperDir,
        [Parameter(Mandatory)][string] $ScratchDir,
        [Parameter(Mandatory)][string] $BuildDir,
        [Parameter(Mandatory)][string] $Rid,
        [Parameter(Mandatory)][string] $BuildInfo,
        [Parameter(Mandatory)][string] $CmakeOptions,
        [string] $ArchOptions = '',
        [string[]] $ExtraCmakeArgs = @()
    )

    if (Test-Path -LiteralPath $BuildDir) { Remove-Item -LiteralPath $BuildDir -Recurse -Force }
    New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

    $arguments = @('-S', $WrapperDir, '-B', $BuildDir, '-G', 'Ninja',
                   "-DLLAMA_SOURCE_DIR=$ScratchDir",
                   "-DCODEBRIX_LLAMA_RID=$Rid",
                   "-DCODEBRIX_LLAMA_BUILD_INFO=$BuildInfo") +
                 ($CmakeOptions -split '\s+' | Where-Object { $_ -ne '' }) +
                 ($ArchOptions -split '\s+' | Where-Object { $_ -ne '' }) +
                 $ExtraCmakeArgs

    Write-Host "  cmake $($arguments -join ' ')"
    & cmake @arguments 2>&1 | Tee-Object -FilePath (Join-Path $BuildDir 'configure.log') | Where-Object { $_ -match 'codebrix_llama:|Warning|Error' } | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE) - see $BuildDir\configure.log" }

    Write-Host "  building (log: $BuildDir\build.log) ..."
    & cmake --build $BuildDir 2>&1 | Tee-Object -FilePath (Join-Path $BuildDir 'build.log') | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath (Join-Path $BuildDir 'build.log') | Select-String -Pattern 'error|FAILED' -Context 2, 8 | Select-Object -First 5 | ForEach-Object { Write-Host $_ }
        throw "build failed (exit $LASTEXITCODE) - see $BuildDir\build.log"
    }
    Write-Host "  built: $(Get-Content -LiteralPath (Join-Path $BuildDir 'build.log') | Select-Object -Last 1)"
}

# ---------------------------------------------------------------------------------------------
# The complete public API, read from the header the library was built from (comment lines
# dropped, header joined into one line so a declaration split across lines is still seen).
# ---------------------------------------------------------------------------------------------
function Get-RequiredSymbolsFromHeader {
    param([Parameter(Mandatory)][string] $HeaderPath)

    $joined = (Get-Content -LiteralPath $HeaderPath | Where-Object { $_ -notmatch '^\s*//' }) -join ' '
    $names = [regex]::Matches($joined, 'LLAMA_API[^;(]*\b(llama_[a-z0-9_]+)\s*\(') | ForEach-Object { $_.Groups[1].Value }
    return ($names | Sort-Object -Unique)
}

$script:ExtraRequiredSymbols = @(
    'ggml_backend_dev_count', 'ggml_backend_dev_get', 'ggml_backend_dev_name',
    'ggml_backend_dev_description', 'ggml_backend_dev_type', 'ggml_backend_dev_memory',
    'ggml_log_set',
    'gguf_init_from_file', 'gguf_free', 'gguf_get_n_kv', 'gguf_get_key', 'gguf_get_val_str', 'gguf_get_n_tensors',
    'codebrix_llama_build_info', 'codebrix_llama_rid'
)

# Only these may appear in `dumpbin /dependents`. Anything from the Visual C++ runtime
# (VCRUNTIME140.dll, MSVCP140.dll, api-ms-win-crt-*.dll) means the static-CRT setting did not
# take, and the package would demand a redistributable on every user's machine. The in-box
# system DLLs that a static-CRT DLL legitimately imports are all listed.
$script:AllowedDependents = @('KERNEL32.dll', 'ADVAPI32.dll', 'USER32.dll', 'bcrypt.dll', 'ntdll.dll', 'WS2_32.dll')

$script:GateFailures = New-Object System.Collections.Generic.List[string]

function Add-GatePass { param([string] $Message) Write-Host "  [ok] $Message" }
function Add-GateFail { param([string] $Message) Write-Host "  [FAIL] $Message"; $script:GateFailures.Add($Message) }

function Invoke-Dumpbin {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = & dumpbin @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "dumpbin $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
    return $output
}

# ---------------------------------------------------------------------------------------------
# The gate. Same checks as the Linux and macOS builds, expressed with the tools Windows has.
# ---------------------------------------------------------------------------------------------
function Test-CodebrixLlamaBinary {
    param(
        [Parameter(Mandatory)][string] $DllPath,              # the STAGED dll (the file the package ships)
        [Parameter(Mandatory)][string] $ExpectedMachine,      # 'x64' or 'ARM64'
        [Parameter(Mandatory)][string] $Rid,
        [Parameter(Mandatory)][string] $BuildDir,             # holds the gate tools and the header copy
        [Parameter(Mandatory)][string] $ModelFile,
        [Parameter(Mandatory)][string] $ModelSha256,
        [Parameter(Mandatory)][string] $ExpectedFile,
        [Parameter(Mandatory)][string] $Tolerance,
        [bool] $CanRunTargetBinaries = $true
    )

    # --- machine type -------------------------------------------------------------------------
    $headers = Invoke-Dumpbin @('/nologo', '/headers', $DllPath)
    $machineLine = $headers | Select-String -Pattern 'machine \(' | Select-Object -First 1
    if ($machineLine -and $machineLine.ToString() -match [regex]::Escape($ExpectedMachine)) {
        Add-GatePass "architecture: $($machineLine.ToString().Trim())"
    }
    else {
        Add-GateFail "architecture mismatch: expected $ExpectedMachine, dumpbin says '$($machineLine)'"
    }

    # --- exports ------------------------------------------------------------------------------
    $exportLines = Invoke-Dumpbin @('/nologo', '/exports', $DllPath)
    $exports = @()
    foreach ($line in $exportLines) {
        if ($line -match '^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)\s*$') { $exports += $Matches[1] }
    }
    $required = @(Get-RequiredSymbolsFromHeader (Join-Path $BuildDir 'llama-source-header.h')) + $script:ExtraRequiredSymbols
    $missing = @($required | Where-Object { $exports -notcontains $_ })
    if ($missing.Count -gt 0) {
        Add-GateFail "missing exports: $($missing -join ' ')"
    }
    else {
        Add-GatePass "all $($required.Count) required symbols exported"
    }
    $foreign = @($exports | Where-Object { $_ -notmatch '^(llama_|ggml_|gguf_|codebrix_llama_)' })
    if ($foreign.Count -gt 0) {
        Add-GateFail "exports outside the llama_/ggml_/gguf_/codebrix_llama_ surface: $(($foreign | Select-Object -First 5) -join ' ') ..."
    }
    else {
        Add-GatePass "export surface is exactly llama_* / ggml_* / gguf_* / codebrix_llama_* ($($exports.Count) symbols)"
    }

    # --- dependents ---------------------------------------------------------------------------
    $dependents = @()
    $inList = $false
    foreach ($line in Invoke-Dumpbin @('/nologo', '/dependents', $DllPath)) {
        $text = $line.ToString().Trim()
        if ($text -like 'Image has the following dependencies*') { $inList = $true; continue }
        if ($inList) {
            if ($text -eq '') { if ($dependents.Count -gt 0) { break } else { continue } }
            if ($text -like 'Summary*') { break }
            $dependents += $text
        }
    }
    $unexpected = @($dependents | Where-Object { $script:AllowedDependents -notcontains $_ })
    if ($unexpected.Count -gt 0) {
        Add-GateFail "unexpected dynamic dependencies: $($unexpected -join ' ') (allowed: $($script:AllowedDependents -join ' ')). Anything from the VC runtime means the static CRT did not take."
    }
    else {
        Add-GatePass "dependencies are system-only: $($dependents -join ' ')"
    }

    if (-not $CanRunTargetBinaries) {
        Add-GateFail 'smoke test NOT RUN - this build targets an architecture this machine cannot execute. (Reported as a failure on purpose: an unrun check is not a passed check.)'
        Add-GateFail 'model regeneration NOT RUN - same reason'
        Add-GateFail 'conformance NOT RUN - same reason'
        return @('  not run: this machine cannot execute the target architecture')
    }

    # THE TOOLS MUST LOAD THE DLL WE JUST BUILT, NOT WHATEVER IS ON PATH. The gate tools sit in
    # the build directory next to the build-tree DLL; copying the STAGED DLL over it puts the
    # exact shipped bytes first in the search order, where PATH cannot displace them. (dav1d's
    # first Windows run was fooled by a GStreamer dav1d.dll on PATH - same lesson.)
    Copy-Item -LiteralPath $DllPath -Destination (Join-Path $BuildDir (Split-Path -Leaf $DllPath)) -Force
    Write-Host '  staged dll copied beside the gate tools so PATH cannot supply a different one'

    # --- LoadLibrary smoke test ---------------------------------------------------------------
    Write-Host '  --- LoadLibrary smoke test ---'
    Push-Location $BuildDir
    try {
        & .\smoke-test.exe (Join-Path $BuildDir (Split-Path -Leaf $DllPath)) $Rid 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -eq 0) { Add-GatePass 'smoke test' } else { Add-GateFail 'smoke test' }
    }
    finally { Pop-Location }

    # --- model regeneration -------------------------------------------------------------------
    Write-Host '  --- model regeneration ---'
    $regen = Join-Path $BuildDir 'regenerated-model.gguf'
    Push-Location $BuildDir
    try {
        & .\generate-test-model.exe $regen 2>&1 | ForEach-Object { Write-Host "    $_" }
        $genExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($genExit -eq 0 -and (Test-Path -LiteralPath $regen)) {
        $regenSha = (Get-FileHash -LiteralPath $regen -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($regenSha -eq $ModelSha256.ToLowerInvariant()) {
            Add-GatePass 'regenerated model is byte-identical to the committed test-vectors model'
        }
        else {
            Add-GateFail "regenerated model sha256 $regenSha differs from the committed $ModelSha256"
        }
    }
    else {
        Add-GateFail 'generate-test-model failed'
    }

    # --- conformance --------------------------------------------------------------------------
    Write-Host '  --- conformance (CPU) ---'
    $summary = New-Object System.Collections.Generic.List[string]
    Push-Location $BuildDir
    try {
        $confOut = & .\conformance-check.exe $ModelFile --check $ExpectedFile --tolerance $Tolerance --gpu-layers 0 2>$null
        $confExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $confOut | ForEach-Object { Write-Host "    $_" }
    $line = ($confOut | Where-Object { $_ -match 'conformance:' } | Select-Object -First 1)
    if ($confExit -eq 0) { Add-GatePass 'conformance on the CPU path' } else { Add-GateFail 'conformance on the CPU path' }
    if ($line) { $summary.Add("  CPU  : $line") } else { $summary.Add('  CPU  : no summary line (the checker did not run to completion)') }

    return $summary
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
