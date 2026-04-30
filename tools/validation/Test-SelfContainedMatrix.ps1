[CmdletBinding()]
param(
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot,
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$projectPath = Join-Path $repoRoot "src\MovieTelopTranscriber.App\MovieTelopTranscriber.App.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "temp\self-contained-matrix"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

$scenarios = @(
    [pscustomobject]@{
        Name = "release-baseline"
        Configuration = "Release"
        PublishTrimmed = $true
        PublishReadyToRun = $true
        WindowsAppSdkDeploymentManagerInitialize = $false
        Notes = "current app baseline"
    },
    [pscustomobject]@{
        Name = "release-no-trim"
        Configuration = "Release"
        PublishTrimmed = $false
        PublishReadyToRun = $true
        WindowsAppSdkDeploymentManagerInitialize = $false
        Notes = "trim disabled"
    },
    [pscustomobject]@{
        Name = "release-no-r2r"
        Configuration = "Release"
        PublishTrimmed = $true
        PublishReadyToRun = $false
        WindowsAppSdkDeploymentManagerInitialize = $false
        Notes = "ready-to-run disabled"
    },
    [pscustomobject]@{
        Name = "release-init-enabled"
        Configuration = "Release"
        PublishTrimmed = $true
        PublishReadyToRun = $true
        WindowsAppSdkDeploymentManagerInitialize = $true
        Notes = "deployment manager init enabled"
    },
    [pscustomobject]@{
        Name = "debug-baseline"
        Configuration = "Debug"
        PublishTrimmed = $false
        PublishReadyToRun = $false
        WindowsAppSdkDeploymentManagerInitialize = $false
        Notes = "debug publish baseline"
    }
)

function Invoke-MatrixScenario {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Scenario
    )

    $outputDirectory = Join-Path $OutputRoot $Scenario.Name
    if (Test-Path -LiteralPath $outputDirectory) {
        Remove-Item -LiteralPath $outputDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    $publishLogPath = Join-Path $outputDirectory "publish.log"

    $publishArgs = @(
        "publish"
        $projectPath
        "-c", $Scenario.Configuration
        "-p:Platform=$Platform"
        "-r", $RuntimeIdentifier
        "--self-contained", "true"
        "-p:PublishTrimmed=$($Scenario.PublishTrimmed.ToString().ToLowerInvariant())"
        "-p:PublishReadyToRun=$($Scenario.PublishReadyToRun.ToString().ToLowerInvariant())"
        "-p:WindowsAppSdkDeploymentManagerInitialize=$($Scenario.WindowsAppSdkDeploymentManagerInitialize.ToString().ToLowerInvariant())"
        "-o", $outputDirectory
    )

    (& dotnet @publishArgs 2>&1 | Tee-Object -FilePath $publishLogPath | Out-Host)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($Scenario.Name) with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $outputDirectory "MovieTelopTranscriber.App.exe"
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Published executable was not found for $($Scenario.Name): $exePath"
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $outputDirectory -Recurse -File | Where-Object { $_.FullName -ne $publishLogPath })
    $totalBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum

    $process = Start-Process -FilePath $exePath -WorkingDirectory $outputDirectory -PassThru
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

    return [pscustomobject]@{
        Scenario = $Scenario.Name
        Configuration = $Scenario.Configuration
        PublishTrimmed = $Scenario.PublishTrimmed
        PublishReadyToRun = $Scenario.PublishReadyToRun
        WindowsAppSdkDeploymentManagerInitialize = $Scenario.WindowsAppSdkDeploymentManagerInitialize
        Notes = $Scenario.Notes
        OutputDirectory = $outputDirectory
        PublishedFileCount = $publishedFiles.Count
        PublishedTotalBytes = $totalBytes
        ProcessStatus = $status
        ExitCode = $exitCode
        ExitCodeHex = if ($null -eq $exitCode) { $null } else { ('0x{0:X8}' -f ($exitCode -band 0xffffffff)) }
        PublishLogPath = $publishLogPath
    }
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputRoot | Out-Null

$results = foreach ($scenario in $scenarios) {
    Invoke-MatrixScenario -Scenario $scenario
}

$results | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $OutputRoot "matrix-results.json")

$results | Format-Table -AutoSize
