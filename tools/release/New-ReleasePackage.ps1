[CmdletBinding()]
param(
    [string]$Version = "0.1.1",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$OutputRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "dist"
}

$targetFramework = "net10.0-windows10.0.26100.0"
$solutionPath = Join-Path $repoRoot "src\MovieTelopTranscriber.sln"
$appBuildDir = Join-Path $repoRoot "src\MovieTelopTranscriber.App\bin\$Platform\$Configuration\$targetFramework"
$packageName = "movie-telop-transcriber-win-x64-v$Version"
$stagingRoot = Join-Path $OutputRoot "staging"
$packageRoot = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $OutputRoot "$packageName.zip"
$checksumPath = "$zipPath.sha256"
$installerAssetPath = Join-Path $OutputRoot "Install-MovieTelopTranscriber.ps1"
$installerChecksumPath = "$installerAssetPath.sha256"
$packageTimestampUtc = [DateTimeOffset]::Parse("2026-04-29T00:00:00Z").UtcDateTime

function Copy-RepoFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourcePath = Join-Path $repoRoot $Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required file was not found: $Source"
    }

    $destinationPath = Join-Path $packageRoot $Destination
    $destinationDir = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

function Copy-RepoFileMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    $sourceDirectory = Join-Path $repoRoot $Directory
    $matches = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter $Filter -File)
    if ($matches.Count -ne 1) {
        throw "Expected one file for $Directory\$Filter, but found $($matches.Count)."
    }

    $destinationPath = Join-Path (Join-Path $packageRoot $DestinationDirectory) $matches[0].Name
    $destinationDir = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -LiteralPath $matches[0].FullName -Destination $destinationPath -Force
}

function Test-PackageFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePattern
    )

    $matches = @(Get-ChildItem -Path (Join-Path $packageRoot $RelativePattern) -File)
    if ($matches.Count -eq 0) {
        throw "Package validation failed. Missing: $RelativePattern"
    }
}

if (-not $SkipBuild) {
    dotnet build $solutionPath -c $Configuration -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

$appExe = Join-Path $appBuildDir "MovieTelopTranscriber.App.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Release build output was not found: $appExe"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}
if (Test-Path -LiteralPath $installerAssetPath) {
    Remove-Item -LiteralPath $installerAssetPath -Force
}
if (Test-Path -LiteralPath $installerChecksumPath) {
    Remove-Item -LiteralPath $installerChecksumPath -Force
}

New-Item -ItemType Directory -Path (Join-Path $packageRoot "app") -Force | Out-Null
Copy-Item -Path (Join-Path $appBuildDir "*") -Destination (Join-Path $packageRoot "app") -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $packageRoot "app") -Recurse -Filter "*.pdb" -File | Remove-Item -Force

Copy-RepoFile -Source "README.md" -Destination "docs\README.md"
Copy-RepoFile -Source "tools\install\Install-MovieTelopTranscriber.ps1" -Destination "Install-MovieTelopTranscriber.ps1"
Copy-RepoFile -Source "tools\install\Install-MovieTelopTranscriber.cmd" -Destination "Install-MovieTelopTranscriber.cmd"
Copy-RepoFileMatch -Directory "docs" -Filter "08_*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs" -Filter "09_Windows_OCR*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs" -Filter "10_PaddleOCR*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs" -Filter "11_*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs" -Filter "12_*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs" -Filter "13_*.md" -DestinationDirectory "docs"
Copy-RepoFileMatch -Directory "docs\spec" -Filter "04_*.md" -DestinationDirectory "docs\spec"
Copy-RepoFileMatch -Directory "docs\spec" -Filter "06_QCDS*.md" -DestinationDirectory "docs\spec"
Copy-RepoFileMatch -Directory "docs\spec" -Filter "07_*.md" -DestinationDirectory "docs\spec"
Copy-RepoFile -Source "docs\templates\qcds_evaluation_report_template.md" -Destination "docs\templates\qcds_evaluation_report_template.md"

Copy-RepoFile -Source "test-data\basic_telop\README.md" -Destination "samples\basic_telop\README.md"
Copy-RepoFile -Source "test-data\basic_telop\sample_basic_telop.mp4" -Destination "samples\basic_telop\sample_basic_telop.mp4"
Copy-RepoFile -Source "test-data\basic_telop\ground_truth.json" -Destination "samples\basic_telop\ground_truth.json"

$requiredPackageFiles = @(
    "app\MovieTelopTranscriber.App.exe",
    "app\MovieTelopTranscriber.App.dll",
    "app\MovieTelopTranscriber.App.deps.json",
    "app\MovieTelopTranscriber.App.runtimeconfig.json",
    "app\tools\ocr\paddle_ocr_worker.py",
    "Install-MovieTelopTranscriber.ps1",
    "Install-MovieTelopTranscriber.cmd",
    "docs\README.md",
    "samples\basic_telop\README.md",
    "samples\basic_telop\sample_basic_telop.mp4",
    "samples\basic_telop\ground_truth.json"
)

foreach ($relativePath in $requiredPackageFiles) {
    Test-PackageFile -RelativePattern $relativePath
}
Test-PackageFile -RelativePattern "docs\12_*.md"
Test-PackageFile -RelativePattern "docs\13_*.md"

$packageItems = @((Get-Item -LiteralPath $packageRoot)) + @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Force)
$packageItems | ForEach-Object {
    $_.CreationTimeUtc = $packageTimestampUtc
    $_.LastAccessTimeUtc = $packageTimestampUtc
    $_.LastWriteTimeUtc = $packageTimestampUtc
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $checksumPath -Encoding ascii

Copy-Item -LiteralPath (Join-Path $repoRoot "tools\install\Install-MovieTelopTranscriber.ps1") -Destination $installerAssetPath -Force
$installerHash = Get-FileHash -LiteralPath $installerAssetPath -Algorithm SHA256
"$($installerHash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $installerAssetPath)" | Set-Content -LiteralPath $installerChecksumPath -Encoding ascii

$fileCount = (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Measure-Object).Count
$expandedSizeBytes = (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
$zipSizeBytes = (Get-Item -LiteralPath $zipPath).Length

[pscustomobject]@{
    Version = $Version
    PackageName = $packageName
    PackageRoot = $packageRoot
    ZipPath = $zipPath
    ChecksumPath = $checksumPath
    InstallerPath = $installerAssetPath
    InstallerChecksumPath = $installerChecksumPath
    FileCount = $fileCount
    ExpandedSizeBytes = $expandedSizeBytes
    ZipSizeBytes = $zipSizeBytes
    Sha256 = $hash.Hash.ToLowerInvariant()
}
