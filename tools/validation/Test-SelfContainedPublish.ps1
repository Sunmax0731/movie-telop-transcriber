[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory,
    [int]$WaitSeconds = 30,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$projectPath = Join-Path $repoRoot "src\MovieTelopTranscriber.App\MovieTelopTranscriber.App.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "temp\self-contained-repro"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$exePath = Join-Path $OutputDirectory "MovieTelopTranscriber.App.exe"

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $OutputDirectory) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }

    dotnet publish $projectPath `
        -c $Configuration `
        -p:Platform=$Platform `
        -r $RuntimeIdentifier `
        --self-contained true `
        -o $OutputDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Published executable was not found: $exePath"
}

$publishedFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File)
$totalBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum

$process = Start-Process -FilePath $exePath -WorkingDirectory $OutputDirectory -PassThru
Start-Sleep -Seconds $WaitSeconds

$exitCode = $null
$status = "running"
if ($process.HasExited) {
    $status = "exited"
    $exitCode = $process.ExitCode
}
else {
    Stop-Process -Id $process.Id -Force
}

[pscustomobject]@{
    Configuration = $Configuration
    Platform = $Platform
    RuntimeIdentifier = $RuntimeIdentifier
    OutputDirectory = $OutputDirectory
    ExecutablePath = $exePath
    WaitSeconds = $WaitSeconds
    PublishedFileCount = $publishedFiles.Count
    PublishedTotalBytes = $totalBytes
    ProcessStatus = $status
    ExitCode = $exitCode
    ExitCodeHex = if ($null -eq $exitCode) { $null } else { ('0x{0:X8}' -f ($exitCode -band 0xffffffff)) }
} | Format-List
