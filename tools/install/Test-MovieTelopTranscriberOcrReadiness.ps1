[CmdletBinding()]
param(
    [string]$InstallRoot,
    [string]$AppSettingsPath,
    [string]$InstallManifestPath,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path (Get-Location).Path "MovieTelopTranscriber"
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
if ([string]::IsNullOrWhiteSpace($AppSettingsPath)) {
    $AppSettingsPath = Join-Path $InstallRoot "app\movie-telop-transcriber.settings.json"
}
if ([string]::IsNullOrWhiteSpace($InstallManifestPath)) {
    $InstallManifestPath = Join-Path $InstallRoot "movie-telop-transcriber.installation.json"
}

$AppSettingsPath = [System.IO.Path]::GetFullPath($AppSettingsPath)
$InstallManifestPath = [System.IO.Path]::GetFullPath($InstallManifestPath)
$knownModelNames = @(
    "PP-OCRv5_server_det",
    "PP-OCRv5_server_rec"
)

function New-CheckResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Detail
    )

    [pscustomobject]@{
        name = $Name
        status = $Status
        detail = $Detail
    }
}

function Get-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}

function Invoke-PythonProbe {
    param(
        [Parameter(Mandatory = $true)][string]$PythonPath,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    $scriptPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), ".py")
    try {
        [System.IO.File]::WriteAllText($scriptPath, $Code, [System.Text.UTF8Encoding]::new($false))
        $process = Start-Process -FilePath $PythonPath -ArgumentList @($scriptPath) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = (Get-Content -LiteralPath $stdoutPath -Raw -Encoding utf8)
            StdErr = (Get-Content -LiteralPath $stderrPath -Raw -Encoding utf8)
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath, $scriptPath -Force -ErrorAction SilentlyContinue
    }
}

$checks = New-Object System.Collections.Generic.List[object]
$settings = Get-JsonFile -Path $AppSettingsPath
$manifest = Get-JsonFile -Path $InstallManifestPath

if ($settings) {
    $checks.Add((New-CheckResult -Name "settings" -Status "ready" -Detail "movie-telop-transcriber.settings.json was found."))
} else {
    $checks.Add((New-CheckResult -Name "settings" -Status "error" -Detail "movie-telop-transcriber.settings.json was not found."))
}

if ($manifest) {
    $checks.Add((New-CheckResult -Name "install_manifest" -Status "ready" -Detail "movie-telop-transcriber.installation.json was found."))
} else {
    $checks.Add((New-CheckResult -Name "install_manifest" -Status "warning" -Detail "movie-telop-transcriber.installation.json was not found."))
}

$pythonPath = $null
$workerScriptPath = $null
if ($settings -and $settings.paddleOcr) {
    $pythonPath = [string]$settings.paddleOcr.pythonPath
    $workerScriptPath = [string]$settings.paddleOcr.scriptPath
}

$modelRoot = $null
if ($manifest) {
    $modelRoot = [string]$manifest.modelRoot
}
if ([string]::IsNullOrWhiteSpace($modelRoot)) {
    $modelRoot = Join-Path $env:USERPROFILE ".paddlex\official_models"
}

if (-not [string]::IsNullOrWhiteSpace($pythonPath) -and (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    $checks.Add((New-CheckResult -Name "python_path" -Status "ready" -Detail "Python path exists: $pythonPath"))
} elseif ([string]::IsNullOrWhiteSpace($pythonPath)) {
    $checks.Add((New-CheckResult -Name "python_path" -Status "error" -Detail "Python path was not configured in settings."))
} else {
    $checks.Add((New-CheckResult -Name "python_path" -Status "error" -Detail "Configured Python path was not found: $pythonPath"))
}

if (-not [string]::IsNullOrWhiteSpace($workerScriptPath) -and (Test-Path -LiteralPath $workerScriptPath -PathType Leaf)) {
    $checks.Add((New-CheckResult -Name "worker_script" -Status "ready" -Detail "Worker script exists: $workerScriptPath"))
} elseif ([string]::IsNullOrWhiteSpace($workerScriptPath)) {
    $checks.Add((New-CheckResult -Name "worker_script" -Status "error" -Detail "Worker script path was not configured in settings."))
} else {
    $checks.Add((New-CheckResult -Name "worker_script" -Status "error" -Detail "Configured worker script was not found: $workerScriptPath"))
}

$pythonVersion = $null
$paddleVersion = $null
$paddleOcrVersion = $null

if (-not [string]::IsNullOrWhiteSpace($pythonPath) -and (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    $probeCode = @'
import json
import sys
import paddle
import paddleocr

print(json.dumps({
    "python": sys.executable,
    "python_version": sys.version.split()[0],
    "paddle": getattr(paddle, "__version__", "unknown"),
    "paddleocr": getattr(paddleocr, "__version__", "unknown"),
}, ensure_ascii=False))
'@

    $probeResult = Invoke-PythonProbe -PythonPath $pythonPath -Code $probeCode
    if ($probeResult.ExitCode -eq 0) {
        $probeText = (($probeResult.StdOut.Trim()) -split "\r?\n" | Select-Object -Last 1)
        $probe = $probeText | ConvertFrom-Json
        $pythonVersion = [string]$probe.python_version
        $paddleVersion = [string]$probe.paddle
        $paddleOcrVersion = [string]$probe.paddleocr
        $checks.Add((New-CheckResult -Name "python_imports" -Status "ready" -Detail "import paddle / paddleocr succeeded."))
    } else {
        $checks.Add((New-CheckResult -Name "python_imports" -Status "error" -Detail ("import paddle / paddleocr failed: {0}" -f (($probeResult.StdErr + "`n" + $probeResult.StdOut).Trim()))))
    }
}

$existingModels = @(
    $knownModelNames |
    Where-Object { Test-Path -LiteralPath (Join-Path $modelRoot $_) -PathType Container }
)
$missingModels = @(
    $knownModelNames |
    Where-Object { -not (Test-Path -LiteralPath (Join-Path $modelRoot $_) -PathType Container) }
)

if ($missingModels.Count -eq 0) {
    $checks.Add((New-CheckResult -Name "paddle_models" -Status "ready" -Detail "Known PaddleOCR model directories were found."))
} elseif ($existingModels.Count -gt 0) {
    $checks.Add((New-CheckResult -Name "paddle_models" -Status "warning" -Detail ("Some PaddleOCR model directories were missing: {0}" -f ($missingModels -join ", "))))
} else {
    $checks.Add((New-CheckResult -Name "paddle_models" -Status "warning" -Detail "Known PaddleOCR model directories were not found. The first OCR may download them if the network is available."))
}

$checkArray = $checks.ToArray()
$checkStatuses = @($checkArray | ForEach-Object { $_.status })

$status = if ($checkStatuses -contains "error") {
    "error"
} elseif ($checkStatuses -contains "warning") {
    "warning"
} else {
    "ready"
}

$result = [pscustomobject]@{
    status = $status
    installRoot = $InstallRoot
    appSettingsPath = $AppSettingsPath
    installManifestPath = $InstallManifestPath
    pythonPath = $pythonPath
    pythonVersion = $pythonVersion
    workerScriptPath = $workerScriptPath
    modelRoot = $modelRoot
    paddleVersion = $paddleVersion
    paddleOcrVersion = $paddleOcrVersion
    missingModels = $missingModels
    checks = $checkArray
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 6
} else {
    $result
}

switch ($status) {
    "ready" { exit 0 }
    "warning" { exit 2 }
    default { exit 1 }
}
