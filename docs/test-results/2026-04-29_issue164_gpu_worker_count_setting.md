# Issue 164 GPU 利用時の OCR worker 数設定

## 概要
- 対象 Issue: `#164`
- 計測日: 2026-04-29
- 目的: GPU 利用時のみ `paddleocr` の worker 数を `1` / `2` で選べるようにし、設定ファイル、設定画面、run log へ反映されることを確認する。

## 実装内容
- `movie-telop-transcriber.settings.json` の `paddleOcr.workerCount` を追加した。
- 設定画面に `OCR worker 数` を追加した。
  - `device` が GPU のときだけ `1` / `2` を選択可能
  - CPU 環境では `1` 固定
- `TelopFrameAnalysisService` に限定並列の OCR 実行経路を追加した。
  - `paddleocr` かつ GPU 利用時のみ複数 client を生成
  - 選別ロジック自体は従来どおり維持
  - `ocr` 実行が必要なフレームだけを worker 数に応じて分配
- `summary.json` / `run.log` に `ocr_worker_count` を追加した。
- インストーラが生成する初期 settings に `workerCount: 1` を追加した。

## 検証
### ビルド
- `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64`

### 設定反映とログ確認
- 一時ハーネス `temp/issue164-smoke` で GPU `1 worker` / `2 workers` をそれぞれ実行
- 共通条件
  - device: `gpu:0`
  - Python: `temp/ocr-eval-gpu/.venv/Scripts/python.exe`
  - 動画: `test-data/basic_telop/botirist.mp4`
  - 対象フレーム: 先頭 20 フレーム

### 結果
| worker 数 | warmup | ocr_total_ms | worker_execution_ms | summary.json | run.log |
| --- | ---: | ---: | ---: | --- | --- |
| 1 | success | 3,426.3 | 3,030.5 | `ocr_worker_count=1` を確認 | `ocr_worker_count=1` を確認 |
| 2 | success | 14,813.6 | 28,662.6 | `ocr_worker_count=2` を確認 | `ocr_worker_count=2` を確認 |

## 観察
1. 設定値は `summary.json` と `run.log` に正しく出力された。
2. GPU でも短い 20 フレーム smoke では `2 workers` の方が遅くなった。
3. `#158` の 40 / 60 フレーム評価では `2 workers` が wall-clock 短縮に寄与していたため、動画長やフレーム数によって損益分岐がある。
4. そのため、既定値を `1` のまま維持し、GPU 利用時のみ利用者が `2` を選べる構成は妥当である。

## 結論
- Issue の受け入れ条件は満たした。
- `2 workers` は GPU で常に高速ではないため、既定値 `1` を維持する判断を再確認した。
