# #95 タイムライン操作と検出設定 UI 追加改善 実装検証

## 対象
- Issue: `#95`
- 実施日: 2026-04-28
- 実施者: Codex
- 対象ブランチ: `issue-95-additional-timeline-ui`

## Desktop 出力先エラー調査
- 対象ログ: `C:\Users\gkkjh\OneDrive\デスクトップ\test\20260428_232814_64d5`
- `output`、`logs`、`segments.json`、`segments.csv`、`frames.csv` は生成済みだった。
- `run.log` では `frame_count=185`、`detection_count=0`、`error_count=185` で、出力先フォルダの書き込み失敗ではなく OCR worker 側の失敗だった。
- OCR response の詳細は `UnicodeDecodeError: 'utf-8' codec can't decode byte 0x83` だった。
- 原因は、日本語を含む Desktop パスを worker へ渡す stdio のエンコーディングが UTF-8 固定ではなかったこと。
- 対応として、`PaddleOcrWorkerClient` の `StandardInputEncoding`、`StandardOutputEncoding`、`StandardErrorEncoding` を UTF-8 に固定した。

## 実装対象
- `◎` などの単独丸記号候補を PaddleOCR worker 側で保守的に正規化する。
- フレーム抽出と OCR の進捗に、経過時間と推定残り時間を表示する。
- 設定画面の固定サイズを `1680 x 720` に変更する。
- タイムライン詳細列から confidence 表示を外し、認識精度列と重複しないようにする。
- プレビューにシーケンスバーを追加し、表示フレームを変更できるようにする。
- 抽出間隔、コントラスト、検出しきい値、検出ボックスしきい値、unclip ratio、limit side length をスライダーで調整できるようにする。
- 設定画面に初期設定へ戻すボタンを追加する。
- 処理状況ペインの下部を左右に分け、左に再実行ボタン、右に直近の失敗を配置する。
- 手動確認フィードバックを受け、選択テキストコピー、列表示 ON/OFF、進捗ラベルの多言語化と毎秒更新、二重丸形状の `◎` 補完、テロップ種類ラベル、設定画面タイトル削除を追加する。

## 自動検証
実行コマンド:

```powershell
dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64
```

結果:
- 成功
- 警告 0
- エラー 0

補助確認:
- `paddle_ocr_worker.py` の記号正規化関数で、`○` と正方形に近い `O` が `◎` に正規化されることを確認した。
- `paddle_ocr_worker.py` の二重丸形状補完関数で、合成した二重丸画像が `◎` に補完されることを確認した。

## 手動確認観点
- Desktop 配下の日本語パスを出力先にして OCR が実行できること。
- 記号 `◎` が検出結果に残ること。
- 処理状況に経過時間と推定残り時間が表示されること。
- 設定画面が `1680 x 720` の固定サイズで表示されること。
- タイムライン詳細列に confidence が重複表示されないこと。
- タイムライン詳細列に出現フレーム数が表示されないこと。
- タイムライン選択テキストをコピーできること。
- タイムライン見出し右クリックで列表示 ON/OFF を切り替えられること。
- 処理状況の経過時間と残り時間が言語設定に応じて表示され、実行中に毎秒更新されること。
- プレビューのシーケンスバーで表示フレームを変更できること。
- 設定画面のスライダーと初期設定へ戻すボタンが意図通り動作すること。
- 処理状況ペイン内で再実行ボタン群と直近の失敗が左右に表示されること。
