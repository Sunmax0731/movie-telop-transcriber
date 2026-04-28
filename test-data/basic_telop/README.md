# basic_telop サンプル

## 目的
OCR と属性解析、セグメント統合、JSON / CSV 出力、ログ出力を確認するための最小サンプル。

## ファイル
- `sample_basic_telop.mp4`: 5 秒、640x360、10 fps の合成動画。
- `ground_truth.json`: 期待セグメント、期待 OCR 検出、表示属性の正解データ。
- `expected_ocr_by_timestamp.json`: 抽出フレーム時刻ごとの OCR サイドカー生成元データ。
- `create_basic_telop_sample.py`: サンプル動画と正解データを再生成するスクリプト。
- `materialize_sidecars.py`: 抽出済みフレームに対応する `.ocr.json` サイドカーを生成するスクリプト。

## 推奨確認手順
1. アプリで `sample_basic_telop.mp4` を選択する。
2. 抽出間隔を `1.0` 秒にして解析を実行する。
3. 実 OCR 評価では、既定の PaddleOCR 経路で `segments.json` を生成する。
4. sidecar 検証を行う場合だけ、`MOVIE_TELOP_OCR_ENGINE=json-sidecar` を明示する。
5. sidecar 検証では、生成された `work/runs/<run_id>/frames/` を引数に `materialize_sidecars.py` を実行する。
6. アプリで `OCR 再実行` を実行する。
7. `segments.json` と `segments.csv` が `ground_truth.json` の期待セグメントと一致するか確認する。
8. QCDS 評価では `tools/validation/evaluate_qcds_report.py` で `ground_truth.json` と `segments.json` を比較する。

## コマンド例
```powershell
python test-data/basic_telop/materialize_sidecars.py work/runs/<run_id>/frames
python tools/validation/evaluate_qcds_report.py `
  --ground-truth test-data/basic_telop/ground_truth.json `
  --segments work/runs/<run_id>/output/segments.json `
  --summary work/runs/<run_id>/logs/summary.json `
  --sample-id basic_telop `
  --output docs/test-results/<date>_qcds_basic_telop_report.md `
  --metrics-output docs/test-results/<date>_qcds_basic_telop_metrics.json
```

注意:
- `materialize_sidecars.py` は `sample_basic_telop.mp4` の検証専用です。
- 任意の動画から抽出した `frames/` に対して実行すると、サンプル動画用の OCR 結果が書き込まれます。
- 任意動画の OCR 精度確認では、このスクリプトを使わず実 OCR ワーカーまたは動画ごとの正解 sidecar を用意してください。
- `json-sidecar` 明示時に sidecar が存在しない場合は、`OCR_SIDECAR_NOT_FOUND` の OCR エラーとして扱われます。

## 前提
- Python で `cv2` と `PIL` を利用できること。
- 日本語描画には Windows の `C:\Windows\Fonts\meiryo.ttc` を使用する。
