# PaddleOCR ワーカー導入手順

## 1. 文書情報
- 対象 Issue: `#72`
- 作成日: 2026-04-28
- 対象工程: リリース
- 記録者: Codex

## 2. 目的
実動画の日本語テロップ認識精度を上げるため、PaddleOCR PP-OCRv5 をアプリ本体の OCR worker として利用する手順を定義する。

## 3. 位置付け
- #72 の採用 OCR は PaddleOCR PP-OCRv5 とする。
- Windows OCR worker は、Python ランタイムやモデルを使えない環境向けの fallback として残す。
- アプリ本体は `MOVIE_TELOP_OCR_ENGINE=paddleocr` のとき `PaddleOcrWorkerClient` を使う。
- `PaddleOcrWorkerClient` は Python worker を stdio で常駐させ、PaddleOCR モデルを 1 回だけ読み込む。
- フレーム単位の `request.json` / `response.json` は既存の共通 OCR 契約のまま `work/runs/<run_id>/ocr/` に保存する。

## 4. Python 環境の準備
開発環境での検証例:

```powershell
python -m venv temp\ocr-eval\.venv
temp\ocr-eval\.venv\Scripts\python.exe -m pip install --upgrade pip
temp\ocr-eval\.venv\Scripts\python.exe -m pip install paddlepaddle==3.2.0 -i https://www.paddlepaddle.org.cn/packages/stable/cpu/
temp\ocr-eval\.venv\Scripts\python.exe -m pip install paddleocr
```

初回実行時に `PP-OCRv5_server_det` と `PP-OCRv5_server_rec` がユーザープロファイル配下へダウンロードされる。オフライン配布でのモデル同梱有無は #48 / #49 で確定する。

## 5. アプリから利用する方法
アプリ起動前に環境変数を設定する。

```powershell
$env:MOVIE_TELOP_OCR_ENGINE = "paddleocr"
$env:MOVIE_TELOP_PADDLEOCR_PYTHON = "D:\Claude\Movie\movie-telop-transcriber\temp\ocr-eval\.venv\Scripts\python.exe"
$env:MOVIE_TELOP_PADDLEOCR_DEVICE = "cpu"
$env:MOVIE_TELOP_PADDLEOCR_MIN_SCORE = "0.5"

Start-Process `
  -FilePath "D:\Claude\Movie\movie-telop-transcriber\src\MovieTelopTranscriber.App\bin\x64\Release\net10.0-windows10.0.26100.0\MovieTelopTranscriber.App.exe" `
  -WorkingDirectory "D:\Claude\Movie\movie-telop-transcriber\src\MovieTelopTranscriber.App\bin\x64\Release\net10.0-windows10.0.26100.0"
```

`paddle_ocr_worker.py` は Release build 時にアプリ出力の `tools/ocr/` へコピーされる。別の場所の worker を使う場合は `MOVIE_TELOP_PADDLEOCR_SCRIPT` にパスを指定する。

## 6. Worker 単体実行
アプリ本体が生成した OCR request JSON と response JSON の出力先を指定して単体実行できる。

```powershell
$env:MOVIE_TELOP_PADDLEOCR_DEVICE = "cpu"

temp\ocr-eval\.venv\Scripts\python.exe tools\ocr\paddle_ocr_worker.py `
  work\runs\<run_id>\ocr\ocr-000031-00001000ms.request.json `
  temp\ocr-eval\paddle-worker-response-000031.json
```

stdio 常駐モードはアプリ本体から利用するためのモードであり、標準出力には制御用 JSON 行だけを返す。

## 7. 設定
| 環境変数 | 内容 | 既定値 |
| --- | --- | --- |
| `MOVIE_TELOP_OCR_ENGINE` | `paddleocr` を指定すると PaddleOCR worker を使う。 | 未指定時は `json-sidecar` または外部 JSON worker |
| `MOVIE_TELOP_PADDLEOCR_PYTHON` | PaddleOCR を導入した Python 実行ファイル。 | `python` |
| `MOVIE_TELOP_PADDLEOCR_SCRIPT` | `paddle_ocr_worker.py` のパス。 | アプリ出力またはリポジトリ内の `tools/ocr/paddle_ocr_worker.py` を探索 |
| `MOVIE_TELOP_PADDLEOCR_DEVICE` | PaddleOCR の推論デバイス。 | `cpu` |
| `MOVIE_TELOP_PADDLEOCR_VERSION` | PaddleOCR の OCR バージョン。 | `PP-OCRv5` |
| `MOVIE_TELOP_PADDLEOCR_LANG` | PaddleOCR の言語指定を固定する場合に使う。 | request の `language_hint` から推定 |
| `MOVIE_TELOP_PADDLEOCR_MIN_SCORE` | この値未満の認識結果を除外する。 | `0.5` |

## 8. 検証結果
`work/runs/20260428_180845_b571/frames/frame_000031_00001000ms.png` を単体実行した結果、以下を検出した。

| テキスト | confidence |
| --- | --- |
| `隠語で脅してくる奴` | `0.9997132420539856` |
| `VS` | `0.8515245914459229` |
| `単語知らないママ` | `0.9988257884979248` |

## 9. 既知制約
- PaddleOCR の Python ランタイムとモデルは現時点では配布物へ同梱していない。
- 初回モデル取得にはネットワーク接続が必要になる。
- CPU 推論では 1 フレームあたり数秒かかる場合がある。
- 大量フレームの速度最適化、モデル同梱、ONNX Runtime 化は #48 / #49 以降で継続判断する。
