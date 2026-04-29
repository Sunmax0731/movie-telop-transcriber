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
- 利用者が起動していたのは、導入先 `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` ではなく、配布 zip 展開フォルダ直下の `app\MovieTelopTranscriber.App.exe` だった。
- 配布 zip 側 `app` には `movie-telop-transcriber.settings.json` が存在しないため、起動時に導入済み OCR runtime の Python を参照できなかった。
- その結果、既定の `py -3.10` で `tools\ocr\paddle_ocr_worker.py` を起動しようとして `paddleocr` import に失敗し、`PADDLEOCR_WORKER_STOPPED` になった。

## 4. 対応
- `AppLaunchSettingsLoader` に fallback を追加し、配布 zip 側 `app` から起動された場合でも、隣接する導入済み `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` を検出して適用するようにした。
- `README.md` と `docs/12_導入手順書.md` に、配布 zip 直下の `app` は導入用同梱物であり、通常起動は導入先 `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` またはスタートメニューを使う旨を追記した。

## 5. 検証
| 項目 | 内容 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors。 |
| 再現ログ確認 | `ocr-000001-00000000ms.response.json` の確認 | `ModuleNotFoundError: No module named 'paddleocr'` を確認。 |
| loader fallback 単体検証 | 一時ハーネスを `package\app` に配置し、隣接する `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` を読ませる | `MOVIE_TELOP_OCR_ENGINE=paddleocr`、`MOVIE_TELOP_PADDLEOCR_PYTHON=C:\installed\python.exe`、`MOVIE_TELOP_PADDLEOCR_SCRIPT=C:\installed\paddle_ocr_worker.py` を確認。 |

## 6. 補足
- この修正は「誤って配布 zip 側の `app` を起動した場合」でも導入済み設定へ寄せるためのもの。
- 通常利用の起動導線は引き続き `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` またはスタートメニューとする。
