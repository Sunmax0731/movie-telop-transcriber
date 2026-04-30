# #203 qcds_suite actual OCR canonical 追加

## 1. 概要
- `test-data/qcds_suite/` の synthetic 代表動画 3 本について、fixture baseline ではなく actual OCR run を使った QCDS canonical を追加した。
- 実行には `tools/validation/OcrPerformanceBenchmark` を使い、`Extract -> OCR -> Segment merge -> Export -> RunLog` を GUI なしで再現した。
- 各 sample の `segments.json` と `logs/summary.json` を `tools/validation/evaluate_qcds_report.py` に渡し、canonical report / metrics を生成した。

## 2. 実行コマンド
```powershell
dotnet run --project tools\validation\OcrPerformanceBenchmark\OcrPerformanceBenchmark.csproj -c Release -- `
  --catalog temp\qcds_actual_suite_catalog.json `
  --output-json temp\qcds_actual_suite_results.json

python tools\validation\evaluate_qcds_report.py `
  --ground-truth test-data\qcds_suite\low_contrast_telop_ground_truth.json `
  --segments temp\ocr-performance-benchmark-runs\20260430_121058_094d\output\segments.json `
  --summary temp\ocr-performance-benchmark-runs\20260430_121058_094d\logs\summary.json `
  --sample-id low_contrast_telop `
  --output docs\test-results\2026-04-30_qcds_low_contrast_telop_actual_report.md `
  --metrics-output docs\test-results\2026-04-30_qcds_low_contrast_telop_actual_metrics.json

python tools\validation\evaluate_qcds_report.py `
  --ground-truth test-data\qcds_suite\multi_position_telop_ground_truth.json `
  --segments temp\ocr-performance-benchmark-runs\20260430_121113_1959\output\segments.json `
  --summary temp\ocr-performance-benchmark-runs\20260430_121113_1959\logs\summary.json `
  --sample-id multi_position_telop `
  --output docs\test-results\2026-04-30_qcds_multi_position_telop_actual_report.md `
  --metrics-output docs\test-results\2026-04-30_qcds_multi_position_telop_actual_metrics.json

python tools\validation\evaluate_qcds_report.py `
  --ground-truth test-data\qcds_suite\short_duration_telop_ground_truth.json `
  --segments temp\ocr-performance-benchmark-runs\20260430_121126_8718\output\segments.json `
  --summary temp\ocr-performance-benchmark-runs\20260430_121126_8718\logs\summary.json `
  --sample-id short_duration_telop `
  --output docs\test-results\2026-04-30_qcds_short_duration_telop_actual_report.md `
  --metrics-output docs\test-results\2026-04-30_qcds_short_duration_telop_actual_metrics.json
```

## 3. 結果サマリ
| sample | Run ID | frame interval | frames | OCR detections | segments | text exact | time delta | OCR total |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `low_contrast_telop` | `20260430_121058_094d` | 1.0 sec | 5 | 4 | 2 | 100.0% | 0 ms | 4.497 sec |
| `multi_position_telop` | `20260430_121113_1959` | 1.0 sec | 5 | 6 | 3 | 100.0% | 0 ms | 4.706 sec |
| `short_duration_telop` | `20260430_121126_8718` | 0.25 sec | 18 | 7 | 3 | 100.0% | 0 ms | 7.160 sec |

## 4. canonical 参照先
- summary:
  [2026-04-30_issue203_qcds_actual_suite.md](2026-04-30_issue203_qcds_actual_suite.md)
- low contrast:
  [2026-04-30_qcds_low_contrast_telop_actual_report.md](2026-04-30_qcds_low_contrast_telop_actual_report.md),
  [2026-04-30_qcds_low_contrast_telop_actual_metrics.json](2026-04-30_qcds_low_contrast_telop_actual_metrics.json)
- multi position:
  [2026-04-30_qcds_multi_position_telop_actual_report.md](2026-04-30_qcds_multi_position_telop_actual_report.md),
  [2026-04-30_qcds_multi_position_telop_actual_metrics.json](2026-04-30_qcds_multi_position_telop_actual_metrics.json)
- short duration:
  [2026-04-30_qcds_short_duration_telop_actual_report.md](2026-04-30_qcds_short_duration_telop_actual_report.md),
  [2026-04-30_qcds_short_duration_telop_actual_metrics.json](2026-04-30_qcds_short_duration_telop_actual_metrics.json)

## 5. 補足
- `tools/validation/OcrPerformanceBenchmark` は `RunPerformanceSummaryRecord` の `OcrWorkerCount` 追加に追従していなかったため、今回の実行前に修正した。
- 今回の追加で、`basic_telop` 以外の synthetic 代表動画も actual OCR run で参照できるようになった。
- 残る QCDS の代表性ギャップは、権利確認済みの実動画 canonical 拡張であり、後続の `#209` で扱う。
