[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Platform = "x64",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TempRoot = ".\temp\release-smoke",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$SkipPackage,
    [switch]$SkipInstall,
    [switch]$AllowReadinessWarning,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Name)
    & $Action
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$tempRootPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $TempRoot))
$smokeRoot = Join-Path $tempRootPath ("v" + $Version.Replace('.', '-'))
$installRoot = Join-Path $smokeRoot "launch-root\MovieTelopTranscriber"
$ocrRuntimeRoot = Join-Path $smokeRoot "ocr-runtime"
$reportPath = Join-Path $smokeRoot "release-smoke-summary.json"
$canonicalPaths = @(
    (Join-Path $repoRoot "docs\test-results\2026-04-29_qcds_basic_telop_rerun_033541_report.md"),
    (Join-Path $repoRoot "docs\test-results\2026-04-30_issue203_qcds_actual_suite.md"),
    (Join-Path $repoRoot "docs\test-results\2026-04-30_issue207_ocr_readiness.md")
)

New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

$buildCommand = "dotnet build src\MovieTelopTranscriber.sln -c $Configuration -p:Platform=$Platform --no-restore"
$testCommand = "dotnet test src\MovieTelopTranscriber.App.Tests\MovieTelopTranscriber.App.Tests.csproj -c $Configuration -m:1"
$packageCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\New-ReleasePackage.ps1 -Version $Version -SkipBuild"
$packageResult = $null
$installResult = $null

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        Invoke-Step "Build app and tools" {
            & dotnet build "src\MovieTelopTranscriber.sln" -c $Configuration -p:Platform=$Platform --no-restore
        }
    }

    if (-not $SkipTests) {
        Invoke-Step "Run tests" {
            & dotnet test "src\MovieTelopTranscriber.App.Tests\MovieTelopTranscriber.App.Tests.csproj" -c $Configuration -m:1
        }
    }

    if (-not $SkipPackage) {
        $packageResult = Invoke-Step "Create release package" {
            $scriptPath = Join-Path $repoRoot "tools\release\New-ReleasePackage.ps1"
            & $scriptPath -Version $Version -SkipBuild |
                Where-Object { $_ -is [psobject] -and $_.PSObject.Properties.Match("ZipPath").Count -gt 0 } |
                Select-Object -Last 1
        }
    }
    else {
        $zipPath = Join-Path $repoRoot ("dist\movie-telop-transcriber-win-x64-v{0}.zip" -f $Version)
        $installerPath = Join-Path $repoRoot "dist\Install-MovieTelopTranscriber.ps1"
        $checksumPath = $zipPath + ".sha256"
        $installerChecksumPath = $installerPath + ".sha256"
        $packageResult = [pscustomobject]@{
            ZipPath = $zipPath
            InstallerPath = $installerPath
            ChecksumPath = $checksumPath
            InstallerChecksumPath = $installerChecksumPath
            Sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    Assert-PathExists -Path $packageResult.ZipPath -Description "Release package zip"
    Assert-PathExists -Path $packageResult.ChecksumPath -Description "Release package checksum"
    Assert-PathExists -Path $packageResult.InstallerPath -Description "Installer script"
    Assert-PathExists -Path $packageResult.InstallerChecksumPath -Description "Installer checksum"

    if (-not $SkipInstall) {
        if (Test-Path -LiteralPath $smokeRoot) {
            Remove-Item -LiteralPath $smokeRoot -Recurse -Force
            New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
        }

        $installResult = Invoke-Step "Install package and run OCR readiness" {
            $installScript = Join-Path $repoRoot "tools\install\Install-MovieTelopTranscriber.ps1"
            & $installScript `
                -PackageZipPath $packageResult.ZipPath `
                -InstallRoot $installRoot `
                -OcrRuntimeRoot $ocrRuntimeRoot `
                -NoStartMenuShortcut `
                -Force |
                Where-Object { $_ -is [psobject] -and $_.PSObject.Properties.Match("AppExe").Count -gt 0 } |
                Select-Object -Last 1
        }

        Assert-PathExists -Path $installResult.AppExe -Description "Installed app executable"
        Assert-PathExists -Path $installResult.AppSettingsPath -Description "Installed app settings"
        Assert-PathExists -Path $installResult.LauncherPath -Description "Installed launcher script"
        Assert-PathExists -Path $installResult.UninstallerPath -Description "Installed uninstaller script"
        Assert-PathExists -Path $installResult.InstallManifestPath -Description "Install manifest"
        Assert-PathExists -Path $installResult.OcrReadinessScriptPath -Description "OCR readiness script"

        if (-not $AllowReadinessWarning -and $installResult.OcrReadinessStatus -ne "ready") {
            throw "OCR readiness status was not ready: $($installResult.OcrReadinessStatus)"
        }
    }

    foreach ($canonicalPath in $canonicalPaths) {
        Assert-PathExists -Path $canonicalPath -Description "Canonical validation artifact"
    }

    $summary = [pscustomobject]@{
        Version = $Version
        Configuration = $Configuration
        Platform = $Platform
        RuntimeIdentifier = $RuntimeIdentifier
        BuildCommand = $buildCommand
        TestCommand = $testCommand
        PackageCommand = $packageCommand
        PackageZipPath = $packageResult.ZipPath
        PackageSha256 = $packageResult.Sha256
        InstallerPath = $packageResult.InstallerPath
        InstallerChecksumPath = $packageResult.InstallerChecksumPath
        InstallRoot = $installRoot
        OcrRuntimeRoot = $ocrRuntimeRoot
        OcrReadinessStatus = if ($installResult) { $installResult.OcrReadinessStatus } else { $null }
        CanonicalArtifacts = $canonicalPaths
        CompletedAt = (Get-Date).ToString("o")
        ReportPath = $reportPath
    }

    $summary | ConvertTo-Json -Depth 6 | Set-Content -Path $reportPath -Encoding utf8

    if ($AsJson) {
        $summary | ConvertTo-Json -Depth 6
    }
    else {
        $summary
    }
}
finally {
    Pop-Location
}
