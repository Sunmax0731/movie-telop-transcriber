# QCDS評価レポート: low_contrast_telop

## 1. 文書情報
- 対象 Issue: `#90`
- 実施日: 2026-04-30
- 代表動画 ID: `low_contrast_telop`
- OCR エンジン: `paddleocr`
- Run ID: `20260430_121058_094d`

## 2. 入力
- 正解データ: `test-data\qcds_suite\low_contrast_telop_ground_truth.json`
- 解析結果: `temp\ocr-performance-benchmark-runs\20260430_121058_094d\output\segments.json`
- サマリ: `temp\ocr-performance-benchmark-runs\20260430_121058_094d\logs\summary.json`

## 3. 再生成コマンド
```powershell
python tools/validation/evaluate_qcds_report.py `
  --ground-truth test-data\qcds_suite\low_contrast_telop_ground_truth.json `
  --segments temp\ocr-performance-benchmark-runs\20260430_121058_094d\output\segments.json `
  --summary temp\ocr-performance-benchmark-runs\20260430_121058_094d\logs\summary.json `
  --sample-id low_contrast_telop `
  --output docs\test-results\2026-04-30_qcds_low_contrast_telop_actual_report.md `
  --metrics-output docs\test-results\2026-04-30_qcds_low_contrast_telop_actual_metrics.json
```

## 4. QCDSサマリ
| 観点 | 指標 | 今回値 | 前回値 | 差分 |
| --- | --- | ---: | ---: | ---: |
| Q | 認識文字列完全一致率 | 100.0% | - | - |
| Q | 文字単位一致率 | 100.0% | - | - |
| Q | 欠落セグメント数 | 0 | - | - |
| Q | 余計な検出セグメント数 | 0 | - | - |
| D | 平均開始時刻誤差 | 0 ms | - | - |
| D | 平均終了時刻誤差 | 0 ms | - | - |
| C | 処理時間 | 4.497 sec | - | - |
| S | エラー件数 | 0 | - | - |

## 5. 件数
- 期待セグメント: 2
- 実セグメント: 2
- マッチしたセグメント: 2
- 欠落セグメント: 0
- 余計な検出セグメント: 0
- フレーム数: 5
- OCR 検出数: 4
- 警告 / エラー: 0 / 0

## 6. セグメント別比較
| 期待ID | 実ID | 期待テキスト | 実テキスト | 文字一致 | 開始誤差 | 終了誤差 | confidence |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| seg-0001 | seg-0001 | 背景になじむ字幕 | 背景になじむ字幕 | 100.0% | 0 ms | 0 ms | 95.1% |
| seg-0002 | seg-0002 | 低コントラスト検証 | 低コントラスト検証 | 100.0% | 0 ms | 0 ms | 84.2% |

## 7. 欠落セグメント
| 期待ID | 期待テキスト | 期待時刻 |
| --- | --- | --- |
| - | - | - |

## 8. 余計な検出セグメント
| 実ID | テキスト | 時刻 | confidence |
| --- | --- | --- | ---: |
| - | - | - | - |

## 9. 自動評価と手動確認の分離
- 自動評価: 文字列一致、文字単位一致、欠落、余計な検出、開始/終了時刻誤差、処理時間、警告/エラー件数。
- 手動確認: プレビュー上の矩形位置、テロップ種別ラベルの妥当性、実利用上の読みやすさ、GUI 操作感。

## 10. 判定メモ
- `low_contrast_telop` の基準比較では、正解セグメントに対して文字列、時刻、件数を確認する。
- 代表動画を追加したときは、このレポートを sample ごとに積み上げて比較する。
- このレポート形式は `--previous-metrics` に前回 JSON を指定すると、改善前後の差分欄を埋められる。
