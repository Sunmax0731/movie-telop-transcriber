# #134 プロジェクトファイル再表示機能 検証結果

## 1. 対象
- Issue: `#134`
- ブランチ: `issue-134-project-file-bundle`
- 検証日: 2026-04-29
- 検証者: Codex

## 2. 実装概要
- メニューバー `ファイル` に `プロジェクトを開く` / `プロジェクトを保存` を追加した。
- `.mtproj` を ZIP ベースの単一ファイルとして定義し、抽出済みフレーム、OCR response、属性解析結果、出力物、UI スナップショットを保存できるようにした。
- 元動画本体は project へ同梱せず、参照パスのみを `project.json` に保存する。
- 元動画パスが失われていても、project 内のフレームとタイムラインを使って再表示できるようにした。

## 3. 検証コマンド
```powershell
dotnet run --project temp\issue134-project-bundle-smoke\issue134-project-bundle-smoke.csproj -p:Platform=x64 -- .
dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64
git diff --check
```

## 4. 検証結果
### 4.1 project 保存と再読込
一時プロジェクト `temp/issue134-project-bundle-smoke` で `work/runs/20260429_100215_bf0f` を入力に使い、保存と再読込を確認した。

```text
normal_frames=185
normal_segments=176
normal_source_exists_at_save=True
normal_selected_segment=seg-0001
normal_selected_detection=paddleocr-000001-00000000ms-01
normal_bundle_has_project_json=True
normal_bundle_has_segments_json=True
normal_bundle_has_first_frame=True
missing_frames=185
missing_segments=176
missing_source_exists_at_save=False
missing_source_exists_now=False
```

確認事項:
- `.mtproj` 保存後に 185 フレーム / 176 セグメントをそのまま再読込できた。
- 保存時の選択セグメントと detection ID を `project.json` に保持できた。
- ZIP 内に `project.json`、`bundle/<run_id>/output/segments.json`、最初のフレーム PNG が存在した。
- 元動画パスが存在しないケースでも、同じ 185 フレーム / 176 セグメントを読み戻せた。

### 4.2 ビルド
`dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` は成功した。

```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```

### 4.3 ドキュメント
- `README.md` に `.mtproj` の機能概要と仕様書リンクを追加した。
- `docs/15_プロジェクトファイル仕様.md` を追加し、保存対象、読み込みフロー、不足動画時の挙動を明記した。

## 5. 結論
- `#134` の完了条件である「再表示できること」「再抽出なしでタイムライン確認できること」「メニューと利用フローの文書化」は満たした。
- 元動画の再指定ダイアログは現時点では未実装であり、不足時は project 内データで再表示を継続する。
