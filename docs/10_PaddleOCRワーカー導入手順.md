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
- アプリ本体は `MOVIE_TELOP_OCR_ENGINE=paddleocr` のとき、または OCR エンジン未指定かつ外部 worker 未指定のとき `PaddleOcrWorkerClient` を使う。
- `json-sidecar` はサンプル sidecar を使う明示的な検証モードとし、利用時は `MOVIE_TELOP_OCR_ENGINE=json-sidecar` を指定する。
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
アプリ起動前に環境変数を設定する。OCR エンジン未指定でも既定では PaddleOCR を使うが、手動検証では利用する Python を明示するため `MOVIE_TELOP_PADDLEOCR_PYTHON` を設定する。

```powershell
$env:MOVIE_TELOP_OCR_ENGINE = "paddleocr"
$env:MOVIE_TELOP_PADDLEOCR_PYTHON = "D:\Claude\Movie\movie-telop-transcriber\temp\ocr-eval\.venv\Scripts\python.exe"
$env:MOVIE_TELOP_PADDLEOCR_DEVICE = "cpu"
$env:MOVIE_TELOP_PADDLEOCR_MIN_SCORE = "0.5"
$env:MOVIE_TELOP_PADDLEOCR_PREPROCESS = "true"
$env:MOVIE_TELOP_PADDLEOCR_CONTRAST = "1.1"
$env:MOVIE_TELOP_PADDLEOCR_SHARPEN = "true"

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
| `MOVIE_TELOP_OCR_ENGINE` | `paddleocr` を指定すると PaddleOCR worker を使う。`json-sidecar` は sidecar 検証時だけ明示する。 | 未指定時は PaddleOCR。ただし `MOVIE_TELOP_OCR_WORKER` がある場合は外部 JSON worker |
| `MOVIE_TELOP_PADDLEOCR_PYTHON` | PaddleOCR を導入した Python 実行ファイル。 | `python` |
| `MOVIE_TELOP_PADDLEOCR_SCRIPT` | `paddle_ocr_worker.py` のパス。 | アプリ出力またはリポジトリ内の `tools/ocr/paddle_ocr_worker.py` を探索 |
| `MOVIE_TELOP_PADDLEOCR_DEVICE` | PaddleOCR の推論デバイス。 | `cpu` |
| `MOVIE_TELOP_PADDLEOCR_VERSION` | PaddleOCR の OCR バージョン。 | `PP-OCRv5` |
| `MOVIE_TELOP_PADDLEOCR_LANG` | PaddleOCR の言語指定を固定する場合に使う。 | request の `language_hint` から推定 |
| `MOVIE_TELOP_PADDLEOCR_MIN_SCORE` | この値未満の認識結果を除外する。 | `0.5` |
| `MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA` | 日本語 OCR 結果で通常サイズとして認識された小書き仮名を補正する。 | `true` |
| `MOVIE_TELOP_PADDLEOCR_PREPROCESS` | OCR 前にフルフレームの自動前処理を行う。手動クロップは行わない。 | `true` |
| `MOVIE_TELOP_PADDLEOCR_CONTRAST` | OCR 前のコントラスト補正倍率。 | `1.1` |
| `MOVIE_TELOP_PADDLEOCR_SHARPEN` | OCR 前に画像全体をシャープ化する。 | `true` |
| `MOVIE_TELOP_PADDLEOCR_TEXT_DET_THRESH` | PaddleOCR の `text_det_thresh` を上書きする。空欄の場合は PaddleOCR 既定値。 | 未指定 |
| `MOVIE_TELOP_PADDLEOCR_TEXT_DET_BOX_THRESH` | PaddleOCR の `text_det_box_thresh` を上書きする。空欄の場合は PaddleOCR 既定値。 | 未指定 |
| `MOVIE_TELOP_PADDLEOCR_TEXT_DET_UNCLIP_RATIO` | PaddleOCR の `text_det_unclip_ratio` を上書きする。空欄の場合は PaddleOCR 既定値。 | 未指定 |
| `MOVIE_TELOP_PADDLEOCR_TEXT_DET_LIMIT_SIDE_LEN` | PaddleOCR の `text_det_limit_side_len` を上書きする。空欄の場合は PaddleOCR 既定値。 | 未指定 |
| `MOVIE_TELOP_PADDLEOCR_USE_TEXTLINE_ORIENTATION` | 文字方向分類を使う。縦書きや回転文字が多い場合に試す。 | `false` |
| `MOVIE_TELOP_PADDLEOCR_USE_DOC_UNWARPING` | 文書ゆがみ補正を使う。通常の動画テロップでは `false` を推奨する。 | `false` |

これらのうち、前処理、コントラスト、シャープ化、検出閾値、文字方向分類、文書ゆがみ補正は設定画面からも変更できる。範囲が明確な項目はスライダーで調整する。拡大率は `1.0` 固定とし、設定画面からは変更しない。変更後に OCR を再実行すると、`PaddleOcrWorkerClient` は設定差分を検出して worker を再起動する。

`PaddleOcrWorkerClient` は Python worker の標準入力、標準出力、標準エラーを UTF-8 固定で扱う。出力先フォルダに日本語を含む場合も、request / response パスは UTF-8 の stdio JSON として渡す。

## 8. 検証結果
`work/runs/20260428_180845_b571/frames/frame_000031_00001000ms.png` を単体実行した結果、以下を検出した。

| テキスト | confidence |
| --- | --- |
| `隠語で脅してくる奴` | `0.9997132420539856` |
| `VS` | `0.8515245914459229` |
| `単語知らないママ` | `0.9988257884979248` |

前処理 ON (`upscale=1.5`、`contrast=1.1`、`sharpen=true`) の過去検証でも同一フレームから以下を検出した。現行設定では拡大率を `1.0` 固定として扱う。

| テキスト | confidence |
| --- | --- |
| `隠語で脅してくる奴` | `0.9891274571418762` |
| `VS` | `0.8761959075927734` |
| `単語知らないママ` | `0.9836950302124023` |

小書き仮名補正の確認では、`ちよつと何ですか` が `ちょっと何ですか`、`急に入つてきて` が `急に入ってきて` として出力された。

## 9. 既知制約
- PaddleOCR の Python ランタイムとモデルは現時点では配布物へ同梱していない。
- 初回モデル取得にはネットワーク接続が必要になる。
- CPU 推論では 1 フレームあたり数秒かかる場合がある。
- 小書き仮名補正は日本語向けの後処理であり、固有名詞や意図的な表記には合わない場合がある。必要に応じて `MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA=false` で無効化できる。
- 単独の `○`、`〇`、`◯`、正方形に近い `O` / `0` は `◎` として保守的に正規化する。通常の英数字列や複数文字列には適用しない。
- PaddleOCR が空文字を返した検出領域でも、二重丸に近い形状であれば `◎` として補完する。
- 前処理のコントラストやシャープ化は動画によって認識結果と処理時間に影響する。拡大率は `1.0` 固定とし、検出が悪化する場合は設定画面または環境変数で `MOVIE_TELOP_PADDLEOCR_PREPROCESS=false` を試す。
- 大量フレームの速度最適化、モデル同梱、ONNX Runtime 化は #48 / #49 以降で継続判断する。
