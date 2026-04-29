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
            [Environment]::SetEnvironmentVariable($Name, $null, "User")
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
            Stop-Process -Id $_.Id -Force
        }
    }
}

function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    $normalizedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return $normalizedPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Start-CleanupScript {
    param(
        [string]$TargetInstallRoot
    )

    $cleanupScriptPath = Join-Path $env:TEMP ("movie-telop-transcriber-uninstall-" + [guid]::NewGuid().ToString("N") + ".cmd")
    $cleanupContent = @"
@echo off
setlocal
set "TARGET_ROOT=$TargetInstallRoot"
for /L %%I in (1,1,20) do (
  if exist "%TARGET_ROOT%" (
    rmdir /S /Q "%TARGET_ROOT%" >nul 2>nul
  )
  if not exist "%TARGET_ROOT%" goto :root_removed
  timeout /t 1 /nobreak >nul
)
:root_removed
del /F /Q "%~f0" >nul 2>nul
"@

    [System.IO.File]::WriteAllText($cleanupScriptPath, $cleanupContent, [System.Text.ASCIIEncoding]::new())
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$cleanupScriptPath`"" -WindowStyle Hidden
}

$manifest = Get-UninstallManifest -Path $manifestPath
$ocrRuntimeRoot = if ($manifest -and $manifest.ocrRuntimeRoot) { [string]$manifest.ocrRuntimeRoot } else { $defaultOcrRuntimeRoot }
$shortcutDirectory = if ($manifest -and $manifest.startMenuShortcutDirectory) { [string]$manifest.startMenuShortcutDirectory } else { $defaultShortcutDirectory }
$shortcutPath = if ($manifest -and $manifest.startMenuShortcutPath) { [string]$manifest.startMenuShortcutPath } else { $defaultShortcutPath }
$launchShortcutPath = if ($manifest -and $manifest.launchShortcutPath) { [string]$manifest.launchShortcutPath } else { $null }
$removeOcrRuntimeRootOnUninstall = [bool]($manifest -and $manifest.removeOcrRuntimeRootOnUninstall)
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
        LaunchShortcutPath = $launchShortcutPath
        RemoveOcrRuntimeRootOnUninstall = $removeOcrRuntimeRootOnUninstall
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
    [Environment]::SetEnvironmentVariable($name, $null, "User")
}

if ($createdModelDirectories.Count -gt 0) {
    foreach ($modelDirectory in $createdModelDirectories) {
        if (Test-Path -LiteralPath $modelDirectory -PathType Container) {
            Remove-Item -LiteralPath $modelDirectory -Recurse -Force
        }
    }
} elseif ($RemoveSharedModelCache -and $manifest -and $manifest.modelRoot) {
    @("PP-OCRv5_server_det", "PP-OCRv5_server_rec") | ForEach-Object {
        $modelDirectory = Join-Path ([string]$manifest.modelRoot) $_
        if (Test-Path -LiteralPath $modelDirectory -PathType Container) {
            Remove-Item -LiteralPath $modelDirectory -Recurse -Force
        }
    }
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

if (-not [string]::IsNullOrWhiteSpace($launchShortcutPath) -and (Test-Path -LiteralPath $launchShortcutPath -PathType Leaf)) {
    Remove-Item -LiteralPath $launchShortcutPath -Force
}

if (Test-Path -LiteralPath $shortcutDirectory -PathType Container) {
    $remaining = @(Get-ChildItem -LiteralPath $shortcutDirectory -Force -ErrorAction SilentlyContinue)
    if ($remaining.Count -eq 0) {
        Remove-Item -LiteralPath $shortcutDirectory -Force
    }
}

if ($removeOcrRuntimeRootOnUninstall -and
    (Test-Path -LiteralPath $ocrRuntimeRoot -PathType Container) -and
    -not (Test-PathWithinRoot -Path $ocrRuntimeRoot -Root $InstallRoot)) {
    Remove-Item -LiteralPath $ocrRuntimeRoot -Recurse -Force
}

Start-CleanupScript -TargetInstallRoot $InstallRoot

[pscustomobject]@{
    InstallRoot = $InstallRoot
    OcrRuntimeRoot = $ocrRuntimeRoot
    ShortcutPath = $shortcutPath
    LaunchShortcutPath = $launchShortcutPath
    RemovedModelDirectories = $createdModelDirectories
}
