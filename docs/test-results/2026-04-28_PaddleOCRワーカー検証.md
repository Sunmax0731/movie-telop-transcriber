# PaddleOCR ワーカー検証

## 1. 文書情報
- 対象 Issue: `#72`
- 作成日: 2026-04-28
- 対象工程: リリース
- 記録者: Codex

## 2. 対象
PaddleOCR PP-OCRv5 worker をアプリ本体の OCR worker として接続し、既存の共通 OCR JSON 契約で動作することを確認する。

## 3. 実装概要
- `MOVIE_TELOP_OCR_ENGINE=paddleocr` の場合、`TelopFrameAnalysisService` は `PaddleOcrWorkerClient` を使う。
- `PaddleOcrWorkerClient` は `tools/ocr/paddle_ocr_worker.py` を stdio で常駐起動する。
- Python worker は PaddleOCR モデルを 1 回だけ読み込み、フレームごとの request/response JSON を処理する。
- `paddle_ocr_worker.py` は Release build 時にアプリ出力の `tools/ocr/` へコピーされる。

## 4. 検証環境
- Python: `Python 3.10.6`
- PaddlePaddle: `3.2.0` CPU
- PaddleOCR: `3.5.0`
- モデル: `PP-OCRv5_server_det`、`PP-OCRv5_server_rec`
- 入力フレーム: `work/runs/20260428_180845_b571/frames/frame_000031_00001000ms.png`

## 5. 検証結果
### 5.1 Release build
```powershell
dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64
```

結果:
- 警告: 0
- エラー: 0

### 5.2 Python worker 単体
```powershell
temp\ocr-eval\.venv\Scripts\python.exe tools\ocr\paddle_ocr_worker.py `
  work\runs\20260428_180845_b571\ocr\ocr-000031-00001000ms.request.json `
  temp\ocr-eval\paddle-worker-response-000031.json
```

結果:
- status: `success`
- detections:
  - `隠語で脅してくる奴` / `0.9997132420539856`
  - `VS` / `0.8515245914459229`
  - `単語知らないママ` / `0.9988257884979248`

### 5.3 アプリ側 client smoke
`temp/PaddleClientSmoke` で `TelopFrameAnalysisService` を呼び出し、`MOVIE_TELOP_OCR_ENGINE=paddleocr` と `MOVIE_TELOP_PADDLEOCR_PYTHON` を指定して検証した。

結果:
- EngineName: `paddleocr`
- status: `success`
- detections:
  - `隠語で脅してくる奴` / `0.9997132420539856`
  - `VS` / `0.8515245914459229`
  - `単語知らないママ` / `0.9988257884979248`

## 6. 残確認
GUI から動画を選択し、抽出からセグメント生成、出力、ログ作成まで手動確認する。手動確認が通過したら #72 を close できる。
