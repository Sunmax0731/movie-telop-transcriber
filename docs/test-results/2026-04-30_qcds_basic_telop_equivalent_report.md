# QCDS評価レポート: basic_telop_equivalent

## 1. 文書情報
- 対象 Issue: `#90`
- 実施日: 2026-04-29
- 代表動画 ID: `basic_telop_equivalent`
- OCR エンジン: `paddleocr`
- Run ID: `20260429_033541_3031`

## 2. 入力
- 正解データ: `D:\Claude\Movie\movie-telop-transcriber\test-data\basic_telop\ground_truth.json`
- 解析結果: `D:\Claude\Movie\movie-telop-transcriber\work\runs\20260429_033541_3031\output\segments.json`
- サマリ: `D:\Claude\Movie\movie-telop-transcriber\work\runs\20260429_033541_3031\logs\summary.json`

## 3. 再生成コマンド
```powershell
python tools/validation/evaluate_qcds_report.py `
  --ground-truth D:\Claude\Movie\movie-telop-transcriber\test-data\basic_telop\ground_truth.json `
  --segments D:\Claude\Movie\movie-telop-transcriber\work\runs\20260429_033541_3031\output\segments.json `
  --summary D:\Claude\Movie\movie-telop-transcriber\work\runs\20260429_033541_3031\logs\summary.json `
  --sample-id basic_telop_equivalent `
  --output D:\Claude\Movie\movie-telop-transcriber\docs\test-results\2026-04-30_qcds_basic_telop_equivalent_report.md `
  --metrics-output D:\Claude\Movie\movie-telop-transcriber\docs\test-results\2026-04-30_qcds_basic_telop_equivalent_metrics.json
```

## 4. QCDSサマリ
| 観点 | 指標 | 今回値 | 前回値 | 差分 |
| --- | --- | ---: | ---: | ---: |
| Q | 認識文字列完全一致率 | 100.0% | 100.0% | 0.0% |
| Q | 文字単位一致率 | 100.0% | 100.0% | 0.0% |
| Q | 欠落セグメント数 | 0 | 0 | 0 |
| Q | 余計な検出セグメント数 | 5 | 5 | 0 |
| D | 平均開始時刻誤差 | 0 ms | 0 ms | 0 ms |
| D | 平均終了時刻誤差 | 0 ms | 0 ms | 0 ms |
| C | 処理時間 | 15.881 sec | 15.881 sec | 0.000 sec |
| S | エラー件数 | 0 | 0 | 0 |

## 5. 件数
- 期待セグメント: 2
- 実セグメント: 7
- マッチしたセグメント: 2
- 欠落セグメント: 0
- 余計な検出セグメント: 5
- フレーム数: 5
- OCR 検出数: 9
- 警告 / エラー: 0 / 0

## 6. セグメント別比較
| 期待ID | 実ID | 期待テキスト | 実テキスト | 文字一致 | 開始誤差 | 終了誤差 | confidence |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| seg-0001 | seg-0003 | サンプルテロップ | サンプルテロップ | 100.0% | 0 ms | 0 ms | 99.6% |
| seg-0002 | seg-0006 | 重要なお知らせ | 重要なお知らせ | 100.0% | 0 ms | 0 ms | 100.0% |

## 7. 欠落セグメント
| 期待ID | 期待テキスト | 期待時刻 |
| --- | --- | --- |
| - | - | - |

## 8. 余計な検出セグメント
| 実ID | テキスト | 時刻 | confidence |
| --- | --- | --- | ---: |
| seg-0001 | 00.0s | 0-1000 ms | 99.8% |
| seg-0002 | 01.0s | 1000-2000 ms | 99.9% |
| seg-0004 | 02.0s | 2000-3000 ms | 100.0% |
| seg-0005 | 03.0s | 3000-4000 ms | 100.0% |
| seg-0007 | 04.0s | 4000-5000 ms | 100.0% |

## 9. 自動評価と手動確認の分離
- 自動評価: 文字列一致、文字単位一致、欠落、余計な検出、開始/終了時刻誤差、処理時間、警告/エラー件数。
- 手動確認: プレビュー上の矩形位置、テロップ種別ラベルの妥当性、実利用上の読みやすさ、GUI 操作感。

## 10. 判定メモ
- `basic_telop_equivalent` の基準比較では、正解セグメントに対して文字列、時刻、件数を確認する。
- 代表動画を追加したときは、このレポートを sample ごとに積み上げて比較する。
- このレポート形式は `--previous-metrics` に前回 JSON を指定すると、改善前後の差分欄を埋められる。
