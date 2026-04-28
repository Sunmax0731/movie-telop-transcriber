# #85 プレビュー overlay ビルド検証

## 対象
- Issue: `#85` 実フレームプレビューと OCR 矩形オーバーレイ
- ブランチ: `issue-85-preview-overlay`
- 実施日: 2026-04-28

## 実施内容
- 中央プレビュー領域に抽出フレーム画像を表示する実装を追加した。
- OCR detection の bounding box と認識文字ラベルを overlay `Canvas` へ描画する実装を追加した。
- タイムライン選択と結果一覧選択からプレビュー対象フレームを同期する実装を追加した。
- `icon.png` から `Assets/AppIcon.ico` と主要 PNG アセットを生成し、exe の `ApplicationIcon` に設定した。
- 設計書、詳細設計、実装メモ、テスト計画へ反映した。

## 検証コマンド
```powershell
dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64
```

## 結果
- 警告: 0
- エラー: 0
- 判定: 成功

## 残確認
- 実フレーム上の矩形位置、選択時の強調表示、画像なし状態は GUI 手動確認で最終判定する。
