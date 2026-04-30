[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MetadataPath,

    [string]$PythonCommand = "python",

    [string]$EvaluateScriptPath = "tools/validation/evaluate_qcds_report.py",

    [string]$ReportOutputPath,

    [string]$MetricsOutputPath,

    [string]$CanonicalNoteOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ToRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $baseUri = [System.Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\') + '\')
    $targetUri = [System.Uri](Resolve-Path -LiteralPath $TargetPath).Path
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\')
}

function Resolve-RequiredPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$metadataFullPath = Resolve-RequiredPath -Path $MetadataPath -Description "Metadata JSON"
$metadata = Get-Content -LiteralPath $metadataFullPath -Encoding UTF8 | ConvertFrom-Json

$sampleId = [string]$metadata.sampleId
if ([string]::IsNullOrWhiteSpace($sampleId)) {
    throw "metadata.sampleId is required."
}

$issueNumber = [string]$metadata.issueNumber
if ([string]::IsNullOrWhiteSpace($issueNumber)) {
    $issueNumber = "210"
}

$captureDate = [string]$metadata.captureDate
if ([string]::IsNullOrWhiteSpace($captureDate)) {
    $captureDate = (Get-Date).ToString("yyyy-MM-dd")
}

$paths = $metadata.paths
if ($null -eq $paths) {
    throw "metadata.paths is required."
}

$groundTruthPath = Resolve-RequiredPath -Path (Join-Path $repoRoot ([string]$paths.groundTruth)) -Description "Ground truth"
$segmentsPath = Resolve-RequiredPath -Path (Join-Path $repoRoot ([string]$paths.segments)) -Description "segments.json"
$summaryPath = Resolve-RequiredPath -Path (Join-Path $repoRoot ([string]$paths.summary)) -Description "summary.json"

$previousMetricsPath = $null
if (-not [string]::IsNullOrWhiteSpace([string]$paths.previousMetrics)) {
    $previousMetricsPath = Resolve-RequiredPath -Path (Join-Path $repoRoot ([string]$paths.previousMetrics)) -Description "previous metrics JSON"
}

if ([string]::IsNullOrWhiteSpace($ReportOutputPath)) {
    $ReportOutputPath = Join-Path $repoRoot ("docs/test-results/{0}_qcds_{1}_report.md" -f $captureDate, $sampleId)
}
elseif (-not [System.IO.Path]::IsPathRooted($ReportOutputPath)) {
    $ReportOutputPath = Join-Path $repoRoot $ReportOutputPath
}

if ([string]::IsNullOrWhiteSpace($MetricsOutputPath)) {
    $MetricsOutputPath = Join-Path $repoRoot ("docs/test-results/{0}_qcds_{1}_metrics.json" -f $captureDate, $sampleId)
}
elseif (-not [System.IO.Path]::IsPathRooted($MetricsOutputPath)) {
    $MetricsOutputPath = Join-Path $repoRoot $MetricsOutputPath
}

if ([string]::IsNullOrWhiteSpace($CanonicalNoteOutputPath)) {
    $CanonicalNoteOutputPath = Join-Path $repoRoot ("docs/test-results/{0}_issue{1}_{2}_canonical.md" -f $captureDate, $issueNumber, $sampleId)
}
elseif (-not [System.IO.Path]::IsPathRooted($CanonicalNoteOutputPath)) {
    $CanonicalNoteOutputPath = Join-Path $repoRoot $CanonicalNoteOutputPath
}

$evaluateScriptFullPath = Resolve-RequiredPath -Path (Join-Path $repoRoot $EvaluateScriptPath) -Description "QCDS evaluation script"

$pythonArguments = @(
    $evaluateScriptFullPath
    "--ground-truth"
    $groundTruthPath
    "--segments"
    $segmentsPath
    "--summary"
    $summaryPath
    "--sample-id"
    $sampleId
    "--output"
    $ReportOutputPath
    "--metrics-output"
    $MetricsOutputPath
)

if ($null -ne $previousMetricsPath) {
    $pythonArguments += @("--previous-metrics", $previousMetricsPath)
}

& $PythonCommand @pythonArguments
if ($LASTEXITCODE -ne 0) {
    throw "evaluate_qcds_report.py failed with exit code $LASTEXITCODE."
}

$video = $metadata.video
$notes = @()
if ($null -ne $metadata.notes) {
    foreach ($note in $metadata.notes) {
        $notes += [string]$note
    }
}

$commandForReport = @(
    "powershell -NoProfile -ExecutionPolicy Bypass"
    "  -File .\tools\validation\New-RealVideoQcdsCanonical.ps1"
    ("  -MetadataPath {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $metadataFullPath))
) -join [Environment]::NewLine

$evaluateCommandForReport = @(
    ("{0} {1}" -f $PythonCommand, (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $evaluateScriptFullPath))
    ("  --ground-truth {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $groundTruthPath))
    ("  --segments {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $segmentsPath))
    ("  --summary {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $summaryPath))
    ("  --sample-id {0}" -f $sampleId)
    ("  --output {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $ReportOutputPath))
    ("  --metrics-output {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $MetricsOutputPath))
)
if ($null -ne $previousMetricsPath) {
    $evaluateCommandForReport += ("  --previous-metrics {0}" -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $previousMetricsPath))
}

$noteLines = New-Object System.Collections.Generic.List[string]
$noteLines.Add(('# Real-video canonical QCDS note `{0}`' -f $sampleId))
$noteLines.Add("")
$noteLines.Add("## 1. Summary")
$noteLines.Add(('- Issue: `#{0}`' -f $issueNumber))
$noteLines.Add(('- Capture date: `{0}`' -f $captureDate))
$noteLines.Add(('- sample ID: `{0}`' -f $sampleId))
$noteLines.Add(('- canonical kind: `{0}`' -f ([string]$metadata.sampleKind)))
$noteLines.Add(('- rights status: `{0}`' -f ([string]$video.rightsStatus)))
$noteLines.Add("")
$noteLines.Add("## 2. Video metadata")
$noteLines.Add(('- File name: `{0}`' -f ([string]$video.fileName)))
$noteLines.Add(('- Duration: `{0}`' -f ([string]$video.duration)))
$noteLines.Add(('- Resolution: `{0}x{1}`' -f ([string]$video.width, [string]$video.height)))
$noteLines.Add(('- Frame interval seconds: `{0}`' -f ([string]$video.frameIntervalSeconds)))
$noteLines.Add(("- Notes: {0}" -f ([string]$video.sourceSummary)))
$noteLines.Add("")
$noteLines.Add("## 3. Inputs")
$noteLines.Add(('- Ground truth: `{0}`' -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $groundTruthPath)))
$noteLines.Add(('- OCR output: `{0}`' -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $segmentsPath)))
$noteLines.Add(('- summary: `{0}`' -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $summaryPath)))
$noteLines.Add("")
$noteLines.Add("## 4. Replay command")
$noteLines.Add('```powershell')
$noteLines.Add($commandForReport)
$noteLines.Add('```')
$noteLines.Add("")
$noteLines.Add("Underlying QCDS evaluation command:")
$noteLines.Add("")
$noteLines.Add('```powershell')
$noteLines.Add(($evaluateCommandForReport -join [Environment]::NewLine))
$noteLines.Add('```')
$noteLines.Add("")
$noteLines.Add("## 5. Outputs")
$noteLines.Add(('- report: `{0}`' -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $ReportOutputPath)))
$noteLines.Add(('- metrics: `{0}`' -f (Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $MetricsOutputPath)))

if ($notes.Count -gt 0) {
    $noteLines.Add("")
$noteLines.Add("## 6. Notes")
    foreach ($note in $notes) {
        $noteLines.Add(("- {0}" -f $note))
    }
}

$noteLines.Add("")
$noteLines.Add("- This canonical keeps Run ID, metadata, and evaluation commands fixed so real measurements can be compared without committing the video binary.")

$canonicalDirectory = Split-Path -Parent $CanonicalNoteOutputPath
if (-not [string]::IsNullOrWhiteSpace($canonicalDirectory)) {
    New-Item -ItemType Directory -Force -Path $canonicalDirectory | Out-Null
}
[System.IO.File]::WriteAllText($CanonicalNoteOutputPath, ($noteLines -join [Environment]::NewLine) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$result = [ordered]@{
    metadata_path = Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $metadataFullPath
    report_path = Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $ReportOutputPath
    metrics_path = Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $MetricsOutputPath
    canonical_note_path = Convert-ToRepoRelativePath -BasePath $repoRoot -TargetPath $CanonicalNoteOutputPath
}

$result | ConvertTo-Json
