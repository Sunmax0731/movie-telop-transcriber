# Issue #125 配布 zip 側 app 起動時 settings fallback 検証

## 1. 文書情報
- 実施日: 2026-04-29
- 対象 Issue: `#125`
- 記録者: Codex

## 2. 事象
- 利用者ログ: `C:\Users\gkkjh\OneDrive\デスクトップ\movie-telop-transcriber-win-x64-v0.1.2\movie-telop-transcriber-win-x64-v0.1.2\app\work\runs\20260429_113122_21d0`
- `run.log` では `ocr_engine=paddleocr`、`error_count=5`
- `ocr\ocr-000001-00000000ms.response.json` では `ModuleNotFoundError: No module named 'paddleocr'`

## 3. 原因
原因は 2 つあった。

1. 配布 zip 側 `app` の誤起動
- 利用者が起動していた経路の 1 つは、導入先 `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` ではなく、配布 zip 展開フォルダ直下の `app\MovieTelopTranscriber.App.exe` だった。
- 配布 zip 側 `app` には `movie-telop-transcriber.settings.json` が存在しないため、起動時に導入済み OCR runtime の Python を参照できなかった。
- その結果、既定の `py -3.10` で `tools\ocr\paddle_ocr_worker.py` を起動しようとして `paddleocr` import に失敗し、`PADDLEOCR_WORKER_STOPPED` になった。

2. 導入先側でも `MOVIE_TELOP_PADDLEOCR_LANG=ja` をそのまま使っていた
- 導入先 `MovieTelopTranscriber\ocr-runtime\.venv\Scripts\python.exe` では `paddleocr` import 自体は成功した。
- しかし通常 OCR リクエストでは `MOVIE_TELOP_PADDLEOCR_LANG=ja` が設定されており、worker の `resolve_language()` はこの環境変数値を正規化せずそのまま返していた。
- PaddleOCR `3.5.0` / `PP-OCRv5` では `lang='ja'` は無効で、`lang='japan'` が必要である。
- その結果、導入先側の run `20260429_113548_2212` / `20260429_114018_5019` では `PADDLEOCR_FAILED`、詳細 `No models are available for the language 'ja' and OCR version 'PP-OCRv5'.` になった。

## 4. 対応
- `AppLaunchSettingsLoader` に fallback を追加し、配布 zip 側 `app` から起動された場合でも、隣接する導入済み `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` を検出して適用するようにした。
- `paddle_ocr_worker.py` の `resolve_language()` を修正し、`MOVIE_TELOP_PADDLEOCR_LANG` で `ja` が与えられた場合も `japan` へ正規化するようにした。
- `README.md` と `docs/12_導入手順書.md` に、配布 zip 直下の `app` は導入用同梱物であり、通常起動は導入先 `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` またはスタートメニューを使う旨を追記した。

## 5. 検証
| 項目 | 内容 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors。 |
| 再現ログ確認 | `ocr-000001-00000000ms.response.json` の確認 | `ModuleNotFoundError: No module named 'paddleocr'` を確認。 |
| 導入先 run 確認 | `20260429_113548_2212` / `20260429_114018_5019` の response 確認 | `No models are available for the language 'ja' and OCR version 'PP-OCRv5'.` を確認。 |
| loader fallback 単体検証 | 一時ハーネスを `package\app` に配置し、隣接する `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` を読ませる | `MOVIE_TELOP_OCR_ENGINE=paddleocr`、`MOVIE_TELOP_PADDLEOCR_PYTHON=C:\installed\python.exe`、`MOVIE_TELOP_PADDLEOCR_SCRIPT=C:\installed\paddle_ocr_worker.py` を確認。 |
| worker 言語正規化検証 | `MOVIE_TELOP_PADDLEOCR_LANG=ja` を設定した状態で実 run request を worker へ手動投入 | 修正前は `PADDLEOCR_FAILED`、修正後は `status=success` で detection を返すことを確認。 |

## 6. 補足
- この修正は「誤って配布 zip 側の `app` を起動した場合」でも導入済み設定へ寄せるためのもの。
- 通常利用の起動導線は引き続き `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` またはスタートメニューとする。
