# Real-video canonical QCDS note `basic_telop_equivalent`

## 1. Summary
- Issue: `#210`
- Capture date: `2026-04-30`
- sample ID: `basic_telop_equivalent`
- canonical kind: `real-video-equivalent`
- rights status: `Equivalent canonical validation using the repo sample video`

## 2. Video metadata
- File name: `sample_basic_telop.mp4`
- Duration: `00:00:05.000`
- Resolution: `640x360`
- Frame interval seconds: `1.0`
- Notes: This validates the workflow for cases where the real video binary stays outside the repo and only metadata plus run artifacts are recorded.

## 3. Inputs
- Ground truth: `test-data\basic_telop\ground_truth.json`
- OCR output: `work\runs\20260429_033541_3031\output\segments.json`
- summary: `work\runs\20260429_033541_3031\logs\summary.json`

## 4. Replay command
```powershell
powershell -NoProfile -ExecutionPolicy Bypass
  -File .\tools\validation\New-RealVideoQcdsCanonical.ps1
  -MetadataPath temp\issue210_basic_telop_equivalent_metadata.json
```

Underlying QCDS evaluation command:

```powershell
python tools\validation\evaluate_qcds_report.py
  --ground-truth test-data\basic_telop\ground_truth.json
  --segments work\runs\20260429_033541_3031\output\segments.json
  --summary work\runs\20260429_033541_3031\logs\summary.json
  --sample-id basic_telop_equivalent
  --output docs\test-results\2026-04-30_qcds_basic_telop_equivalent_report.md
  --metrics-output docs\test-results\2026-04-30_qcds_basic_telop_equivalent_metrics.json
  --previous-metrics docs\test-results\2026-04-29_qcds_basic_telop_rerun_033541_metrics.json
```

## 5. Outputs
- report: `docs\test-results\2026-04-30_qcds_basic_telop_equivalent_report.md`
- metrics: `docs\test-results\2026-04-30_qcds_basic_telop_equivalent_metrics.json`

## 6. Notes
- This run is only for validating the real-video-equivalent workflow.
- For a rights-cleared real video, replace only the metadata and run artifact paths and reuse the same script.

- This canonical keeps Run ID, metadata, and evaluation commands fixed so real measurements can be compared without committing the video binary.
