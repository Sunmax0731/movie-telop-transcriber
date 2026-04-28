# #93 タイムライン編集と設定整理 実装検証

## 対象
- Issue: `#93`
- 実施日: 2026-04-28
- 実施者: Codex
- 対象ブランチ: `issue-93-timeline-edit-settings-cleanup`

## 実装対象
- 認識精度ゲージの 50% 起点表示。
- タイムライン選択中テロップの編集 / 削除操作。
- タイムライン見出しラベルの中央揃え。
- プレビュー再生 / 一時停止。
- 出力先フォルダの事前検証。
- 設定画面の固定サイズ化。
- 左ペインと設定画面の表示責務整理。
- メイン画面の設定ボタン移動。

## Desktop 出力先エラー調査
- 最新ランとして `work/runs/20260428_221813_f9aa` を確認した。
- `logs/run.log` と `logs/summary.json` は成功終了を示しており、Desktop 出力先の失敗ログは標準ランディレクトリ内には残っていなかった。
- `C:\Users\gkkjh\Desktop` と OneDrive Desktop 配下に標準 `run.log` / `summary.json` は確認できなかった。
- 失敗はランディレクトリ作成または出力先解決の前段で発生した可能性があるため、解析開始前に出力先フォルダを絶対パス化し、作成とテスト書き込みを行う事前検証を追加した。
- 事前検証に失敗した場合は `OUTPUT_ROOT_UNAVAILABLE` として GUI の処理状況に表示する。

追記:
- `work/runs/20260428_231345_badd` と `C:\Users\gkkjh\OneDrive\デスクトップ\test\20260428_231633_3038` を確認した。
- どちらもランディレクトリ、`output`、`logs`、`segments.json`、`segments.csv`、`frames.csv` は生成済みだった。
- 共通して `frame_count=185`、`detection_count=0`、`error_count=185` であり、出力先フォルダの書き込み失敗ではなく OCR worker 側の失敗だった。
- OCR response の詳細は `ModuleNotFoundError: No module named 'paddleocr'` で、`MOVIE_TELOP_OCR_ENGINE=paddleocr` だけを指定して起動し、PaddleOCR を導入した Python を `MOVIE_TELOP_PADDLEOCR_PYTHON` で指定していなかったことが原因。
- `temp\ocr-eval\.venv\Scripts\python.exe` では `paddleocr 3.5.0` の import を確認済み。

## 設定画面サイズ調整
- 固定サイズを `1360 x 760` から `1680 x 960` へ変更した。
- その後、#95 の追加要望で縦方向を短縮し、`1680 x 860` へ変更した。
- 目的は、設定画面に縦横スクロールバーが表示されない横長レイアウトにすること。

## 自動検証
実行コマンド:

```powershell
dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64
```

結果:
- 成功
- 警告 0
- エラー 0

## 手動確認観点
- 認識精度 50% 未満の行でゲージが非表示になること。
- 認識精度 50% 以上の行で、50% を最小、100% を最大とする赤から緑のゲージが表示されること。
- タイムライン行選択時に編集 / 削除ボタンが表示されること。
- 編集ボタンでテキスト列にフォーカスが移り、編集確定後に現在結果へ反映されること。
- 削除ボタンで確認ダイアログが表示され、キャンセルと削除が分岐すること。
- プレビュー再生 / 一時停止で抽出済みフレームが順送りされること。
- 設定画面をリサイズまたは最大化できないこと。
- 左ペインからログ、作業フォルダ、言語、抽出間隔、出力ファイル表示が除外され、出力フォルダのみコピー対象として残っていること。
- 出力先フォルダを Desktop に設定しても、書き込み可能な場合は解析が継続し、不可の場合は `OUTPUT_ROOT_UNAVAILABLE` が表示されること。
