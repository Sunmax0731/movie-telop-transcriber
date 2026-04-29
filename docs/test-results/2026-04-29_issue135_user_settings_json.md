# Issue 135 利用者設定 JSON 統合

## 対象
- 対象 Issue: `#135`
- 実施日: 2026-04-29

## 目的
- `movie-telop-transcriber.settings.json` を、OCR 起動専用ではなく、利用者が調整する主要設定の保存先としても使えるように整理する。
- UI から変更する設定と、起動時に読む設定の保存場所を 1 つに揃える。

## 実装内容
- `AppLaunchSettings` に `ui` セクションを追加した。
  - `language`
  - `frameIntervalSeconds`
  - `outputRootDirectory`
  - `mainWindow.width`
  - `mainWindow.height`
- `AppLaunchSettingsLoader` を読み込み専用から、`Load` / `Apply` / `Save` を持つ設定ストアとして整理した。
- `MainPageViewModel` は起動時に `ui` セクションを反映し、言語、抽出間隔、出力先、OCR 調整値の変更時に同じ JSON へ保存するようにした。
- `MainWindowLayoutStore` は従来の別ファイル保存に加えて、優先的に `ui.mainWindow` を使うようにし、終了時は同じ JSON へ保存するようにした。
- インストーラが生成する `movie-telop-transcriber.settings.json` にも `ui` 初期値を書き込むようにした。
- 設定仕様を `docs/14_設定ファイル仕様.md` として追加した。

## 保存対象
- 表示言語
- 抽出間隔
- 出力先フォルダ
- PaddleOCR 前処理
- PaddleOCR 検出しきい値
- 最小文字サイズ
- メイン画面サイズ

## 読み込みと互換性
- 既存の OCR 設定だけを持つ JSON もそのまま読める。
- `ui` セクションがない場合は既定値で起動する。
- 配布 zip 側 `app` からの起動時は、従来どおり隣接する導入済み `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` fallback を維持する。
- 旧 `main-window-layout.json` は、`ui.mainWindow` が未設定のときだけ fallback として読む。

## 検証
| 項目 | コマンド | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors |
| 差分整合 | `git diff --check` | 成功 |

## 補足
- 今回は WinUI 画面の手動操作までは未実施で、コード経路と build を中心に確認した。
- 次の `#134` では、この設定 JSON を前提にプロジェクトファイル側の保存責務を分離しやすくなった。
