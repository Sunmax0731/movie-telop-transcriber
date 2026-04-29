# 2026-04-30 Issue 174 Preview Popup And Layout Adjustment

## 実施内容
- 設定画面の初期高さを `950px` へ拡張
- メイン画面右ペインの最低幅を `750px` へ拡張し、右ペイン host の `MinWidth` を `740px` に更新
- 初回起動時のメイン画面幅を `1800px` に変更
- プレビューを別画面へ表示するアイコンボタンを再生ボタン横へ追加
- プレビュー別画面を閉じるとメイン画面側へプレビュー表示が戻るようにした
- プレビュー別画面表示中は中央ペイン幅を `380px` に固定して、最小幅寄りのレイアウトへ切り替えるようにした
- 既存のヘルプ / ライセンス周辺に残っていた文字化け文字列を正常化した

## 実装メモ
- `PreviewFrameView` をコード構築の共通コントロールとして追加し、メイン画面とポップアップ画面で同じ描画ロジックを共有
- `PreviewWindow` を追加し、同一 `MainPageViewModel` のプレビュー状態を別ウィンドウで表示

## 検証
- `dotnet build .\\src\\MovieTelopTranscriber.sln -c Release -p:Platform=x64`
- `git diff --check`
- Release build の EXE 起動確認
