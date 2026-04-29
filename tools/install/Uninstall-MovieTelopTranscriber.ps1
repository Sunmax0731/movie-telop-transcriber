[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$InstallRoot,
    [switch]$RemoveSharedModelCache
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$manifestPath = Join-Path $InstallRoot "movie-telop-transcriber.installation.json"
$appDir = Join-Path $InstallRoot "app"
$appExe = Join-Path $appDir "MovieTelopTranscriber.App.exe"
$defaultOcrRuntimeRoot = Join-Path $InstallRoot "ocr-runtime"
$defaultShortcutDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Movie Telop Transcriber"
$defaultShortcutPath = Join-Path $defaultShortcutDirectory "Movie Telop Transcriber.lnk"
$legacyUserEnvironmentVariables = @(
    "MOVIE_TELOP_OCR_WORKER",
    "MOVIE_TELOP_PADDLEOCR_PYTHON",
    "MOVIE_TELOP_PADDLEOCR_SCRIPT"
)

function Get-UninstallManifest {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    } catch {
        Write-Warning "Installation manifest could not be read: $Path"
        return $null
    }
}

function Remove-UserEnvironmentVariableIfOwned {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$OwnedRoots = @()
    )

    $value = [Environment]::GetEnvironmentVariable($Name, "User")
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $false
    }

    foreach ($root in $OwnedRoots) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }

        $expandedValue = [Environment]::ExpandEnvironmentVariables($value)
        if (-not [System.IO.Path]::IsPathRooted($expandedValue)) {
            continue
        }

        $normalizedValue = [System.IO.Path]::GetFullPath($expandedValue)
        $normalizedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if ($normalizedValue.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($PSCmdlet.ShouldProcess("User environment variable $Name", "Remove")) {
                [Environment]::SetEnvironmentVariable($Name, $null, "User")
            }
            return $true
        }
    }

    return $false
}

function Stop-InstalledProcesses {
    param([string]$ExpectedExePath)

    $expected = [System.IO.Path]::GetFullPath($ExpectedExePath)
    Get-Process MovieTelopTranscriber.App -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $processPath = $_.Path
        } catch {
            $processPath = $null
        }

        if (-not [string]::IsNullOrWhiteSpace($processPath) -and
            [string]::Equals([System.IO.Path]::GetFullPath($processPath), $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($PSCmdlet.ShouldProcess("Process $($_.Id)", "Stop")) {
                Stop-Process -Id $_.Id -Force
            }
        }
    }
}

function Start-CleanupScript {
    param(
        [string]$TargetInstallRoot,
        [string]$ShortcutDirectory,
        [string]$ShortcutPath
    )

    $cleanupScriptPath = Join-Path $env:TEMP ("movie-telop-transcriber-uninstall-" + [guid]::NewGuid().ToString("N") + ".cmd")
    $cleanupContent = @"
@echo off
setlocal
set "TARGET_ROOT=$TargetInstallRoot"
set "SHORTCUT_DIRECTORY=$ShortcutDirectory"
set "SHORTCUT_PATH=$ShortcutPath"
for /L %%I in (1,1,20) do (
  if exist "%TARGET_ROOT%" (
    rmdir /S /Q "%TARGET_ROOT%" >nul 2>nul
  )
  if not exist "%TARGET_ROOT%" goto :root_removed
  timeout /t 1 /nobreak >nul
)
:root_removed
if exist "%SHORTCUT_PATH%" del /F /Q "%SHORTCUT_PATH%" >nul 2>nul
if exist "%SHORTCUT_DIRECTORY%" rmdir /Q "%SHORTCUT_DIRECTORY%" >nul 2>nul
del /F /Q "%~f0" >nul 2>nul
"@

    [System.IO.File]::WriteAllText($cleanupScriptPath, $cleanupContent, [System.Text.ASCIIEncoding]::new())
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$cleanupScriptPath`"" -WindowStyle Hidden
}

$manifest = Get-UninstallManifest -Path $manifestPath
$ocrRuntimeRoot = if ($manifest -and $manifest.ocrRuntimeRoot) { [string]$manifest.ocrRuntimeRoot } else { $defaultOcrRuntimeRoot }
$shortcutDirectory = if ($manifest -and $manifest.startMenuShortcutDirectory) { [string]$manifest.startMenuShortcutDirectory } else { $defaultShortcutDirectory }
$shortcutPath = if ($manifest -and $manifest.startMenuShortcutPath) { [string]$manifest.startMenuShortcutPath } else { $defaultShortcutPath }
$createdModelDirectories = @()
if ($manifest -and $manifest.createdModelDirectories) {
    $createdModelDirectories = @($manifest.createdModelDirectories | ForEach-Object { [string]$_ })
}
$persistentEnvironmentVariables = @()
if ($manifest -and $manifest.persistentEnvironmentVariables) {
    $persistentEnvironmentVariables = @($manifest.persistentEnvironmentVariables | ForEach-Object { [string]$_ })
}
$ownedRoots = @($InstallRoot, $ocrRuntimeRoot, $appDir)

if ($WhatIfPreference) {
    [pscustomobject]@{
        Mode = "WhatIf"
        InstallRoot = $InstallRoot
        OcrRuntimeRoot = $ocrRuntimeRoot
        ShortcutDirectory = $shortcutDirectory
        ShortcutPath = $shortcutPath
        CreatedModelDirectories = $createdModelDirectories
        RemoveSharedModelCache = [bool]$RemoveSharedModelCache
    }
    return
}

Stop-InstalledProcesses -ExpectedExePath $appExe

foreach ($name in $legacyUserEnvironmentVariables) {
    Remove-UserEnvironmentVariableIfOwned -Name $name -OwnedRoots $ownedRoots | Out-Null
}

foreach ($name in $persistentEnvironmentVariables) {
    if ($PSCmdlet.ShouldProcess("User environment variable $name", "Remove")) {
        [Environment]::SetEnvironmentVariable($name, $null, "User")
    }
}

if ($createdModelDirectories.Count -gt 0) {
    foreach ($modelDirectory in $createdModelDirectories) {
        if (Test-Path -LiteralPath $modelDirectory -PathType Container) {
            if ($PSCmdlet.ShouldProcess($modelDirectory, "Remove model directory")) {
                Remove-Item -LiteralPath $modelDirectory -Recurse -Force
            }
        }
    }
} elseif ($RemoveSharedModelCache -and $manifest -and $manifest.modelRoot) {
    @("PP-OCRv5_server_det", "PP-OCRv5_server_rec") | ForEach-Object {
        $modelDirectory = Join-Path ([string]$manifest.modelRoot) $_
        if (Test-Path -LiteralPath $modelDirectory -PathType Container) {
            if ($PSCmdlet.ShouldProcess($modelDirectory, "Remove shared model directory")) {
                Remove-Item -LiteralPath $modelDirectory -Recurse -Force
            }
        }
    }
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    if ($PSCmdlet.ShouldProcess($shortcutPath, "Remove shortcut")) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

if (Test-Path -LiteralPath $shortcutDirectory -PathType Container) {
    $remaining = @(Get-ChildItem -LiteralPath $shortcutDirectory -Force -ErrorAction SilentlyContinue)
    if ($remaining.Count -eq 0) {
        if ($PSCmdlet.ShouldProcess($shortcutDirectory, "Remove shortcut directory")) {
            Remove-Item -LiteralPath $shortcutDirectory -Force
        }
    }
}

Start-CleanupScript -TargetInstallRoot $InstallRoot -ShortcutDirectory $shortcutDirectory -ShortcutPath $shortcutPath

[pscustomobject]@{
    InstallRoot = $InstallRoot
    OcrRuntimeRoot = $ocrRuntimeRoot
    ShortcutPath = $shortcutPath
    RemovedModelDirectories = $createdModelDirectories
}
