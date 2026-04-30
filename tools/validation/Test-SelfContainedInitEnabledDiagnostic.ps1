[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory,
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$projectPath = Join-Path $repoRoot "src\MovieTelopTranscriber.App\MovieTelopTranscriber.App.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "temp\self-contained-init-diagnostic"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$exePath = Join-Path $OutputDirectory "MovieTelopTranscriber.App.exe"

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$publishLogPath = Join-Path $OutputDirectory "publish.log"
$eventJsonPath = Join-Path $OutputDirectory "event-log.json"
$eventTextPath = Join-Path $OutputDirectory "event-log.txt"

$publishArgs = @(
    "publish"
    $projectPath
    "-c", $Configuration
    "-p:Platform=$Platform"
    "-r", $RuntimeIdentifier
    "--self-contained", "true"
    "-p:WindowsAppSdkDeploymentManagerInitialize=true"
    "-o", $OutputDirectory
)

(& dotnet @publishArgs 2>&1 | Tee-Object -FilePath $publishLogPath | Out-Host)
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Published executable was not found: $exePath"
}

$startTime = (Get-Date).AddSeconds(-5)
$process = Start-Process -FilePath $exePath -WorkingDirectory $OutputDirectory -PassThru
Start-Sleep -Seconds $WaitSeconds
$endTime = Get-Date

$exitCode = $null
$status = "running"
if ($process.HasExited) {
    $status = "exited"
    $exitCode = $process.ExitCode
}
else {
    Stop-Process -Id $process.Id -Force
}

$providers = @(
    ".NET Runtime",
    "Application Error",
    "Windows Error Reporting"
)

$events = foreach ($provider in $providers) {
    Get-WinEvent -FilterHashtable @{
        LogName = "Application"
        ProviderName = $provider
        StartTime = $startTime
        EndTime = $endTime.AddMinutes(1)
    } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Message -match "MovieTelopTranscriber\.App" -or
            $_.Message -match "Movie Telop Transcriber" -or
            $_.Message -match "KERNELBASE\.dll" -or
            $_.Id -in 1000, 1001, 1026
        } |
        Select-Object TimeCreated, ProviderName, Id, LevelDisplayName, MachineName, Message
}

$events = @($events | Sort-Object TimeCreated)
$events | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $eventJsonPath

$eventTextLines = New-Object System.Collections.Generic.List[string]
foreach ($event in $events) {
    $eventTextLines.Add("TimeCreated: $($event.TimeCreated.ToString('o'))")
    $eventTextLines.Add("ProviderName: $($event.ProviderName)")
    $eventTextLines.Add("Id: $($event.Id)")
    $eventTextLines.Add("Level: $($event.LevelDisplayName)")
    $eventTextLines.Add("Message:")
    $eventTextLines.Add($event.Message)
    $eventTextLines.Add("")
}

$eventTextLines | Set-Content -Encoding UTF8 $eventTextPath

[pscustomobject]@{
    Configuration = $Configuration
    Platform = $Platform
    RuntimeIdentifier = $RuntimeIdentifier
    OutputDirectory = $OutputDirectory
    ExecutablePath = $exePath
    WaitSeconds = $WaitSeconds
    ProcessStatus = $status
    ExitCode = $exitCode
    ExitCodeHex = if ($null -eq $exitCode) { $null } else { ('0x{0:X8}' -f ($exitCode -band 0xffffffff)) }
    EventCount = $events.Count
    PublishLogPath = $publishLogPath
    EventJsonPath = $eventJsonPath
    EventTextPath = $eventTextPath
} | Format-List
