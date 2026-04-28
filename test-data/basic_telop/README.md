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
3. 初回は OCR サイドカーがないため、空検出で出力される。
4. 生成された `work/runs/<run_id>/frames/` を引数に `materialize_sidecars.py` を実行する。
5. アプリで `Rerun OCR` を実行する。
6. `segments.json` と `segments.csv` が `ground_truth.json` の期待セグメントと一致するか確認する。

## コマンド例
```powershell
python test-data/basic_telop/materialize_sidecars.py work/runs/<run_id>/frames
```

## 前提
- Python で `cv2` と `PIL` を利用できること。
- 日本語描画には Windows の `C:\Windows\Fonts\meiryo.ttc` を使用する。
