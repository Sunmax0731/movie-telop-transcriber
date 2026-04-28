# QCDS評価レポート: <sample_id>

## 1. 文書情報
- 対象 Issue: `<issue_number>`
- 実施日: `<yyyy-mm-dd>`
- 代表動画 ID: `<sample_id>`
- OCR エンジン: `<ocr_engine>`
- Run ID: `<run_id>`

## 2. 入力
- 正解データ: `<ground_truth.json>`
- 解析結果: `<segments.json>`
- サマリ: `<summary.json>`
- 入力動画メタ情報:
  - ファイル名: `<file_name>`
  - 長さ: `<duration>`
  - 解像度: `<width>x<height>`
  - 抽出間隔: `<frame_interval_seconds>`

## 3. 再生成コマンド
```powershell
python tools/validation/evaluate_qcds_report.py `
  --ground-truth <ground_truth.json> `
  --segments <segments.json> `
  --summary <summary.json> `
  --sample-id <sample_id> `
  --output docs/test-results/<date>_qcds_<sample_id>_report.md `
  --metrics-output docs/test-results/<date>_qcds_<sample_id>_metrics.json
```

前回結果と比較する場合:

```powershell
python tools/validation/evaluate_qcds_report.py `
  --ground-truth <ground_truth.json> `
  --segments <segments.json> `
  --summary <summary.json> `
  --sample-id <sample_id> `
  --previous-metrics docs/test-results/<previous>_qcds_<sample_id>_metrics.json `
  --output docs/test-results/<date>_qcds_<sample_id>_report.md `
  --metrics-output docs/test-results/<date>_qcds_<sample_id>_metrics.json
```

## 4. QCDSサマリ
| 観点 | 指標 | 今回値 | 前回値 | 差分 | 判定 |
| --- | --- | ---: | ---: | ---: | --- |
| Q | 認識文字列完全一致率 |  |  |  |  |
| Q | 文字単位一致率 |  |  |  |  |
| Q | 欠落セグメント数 |  |  |  |  |
| Q | 余計な検出セグメント数 |  |  |  |  |
| D | 平均開始時刻誤差 |  |  |  |  |
| D | 平均終了時刻誤差 |  |  |  |  |
| C | 処理時間 |  |  |  |  |
| S | エラー件数 |  |  |  |  |

## 5. セグメント別比較
| 期待ID | 実ID | 期待テキスト | 実テキスト | 文字一致 | 開始誤差 | 終了誤差 | confidence |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
|  |  |  |  |  |  |  |  |

## 6. 余計な検出と欠落
余計な検出:
- `<actual_segment_id>`: `<text>` / `<time>`

欠落:
- `<expected_segment_id>`: `<text>` / `<time>`

## 7. 手動確認
| 確認項目 | 結果 | メモ |
| --- | --- | --- |
| プレビュー overlay の位置 |  |  |
| タイムライン選択とプレビュー同期 |  |  |
| テロップ種別ラベル |  |  |
| GUI 操作 |  |  |
| 実利用上の見落とし |  |  |

## 8. 判断メモ
- 継続評価上の注意:
- 次に改善する指標:
- 後続 Issue:
