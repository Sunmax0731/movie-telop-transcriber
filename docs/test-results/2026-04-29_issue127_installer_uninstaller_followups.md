# Issue #127 インストーラ / アンインストーラ改善検証

## 1. 実施情報
- 実施日: 2026-04-29
- 対象 Issue: `#127`
- 実施者: Codex

## 2. 対応内容
- インストーラを起動したディレクトリに、導入後の `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` を指す `Movie Telop Transcriber.lnk` を作成するようにした。
- install manifest に、インストーラ実行ディレクトリ、作成した起動ショートカットパス、外部 `ocr-runtime` をアンインストール時に削除すべきかどうかを記録するようにした。
- アンインストーラで、インストーラ実行ディレクトリに作成した `Movie Telop Transcriber.lnk`、導入時に新規作成した外部 `ocr-runtime`、スタートメニューショートカット、導入先フォルダを削除するようにした。
- `WhatIf` 実行時に、作成 / 削除対象が確認できるよう `LaunchShortcutPath` などを出力するようにした。

## 3. 検証項目
| 項目 | 手順 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors。 |
| インストーラ WhatIf | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\install\Install-MovieTelopTranscriber.ps1 -PackageZipPath dist\movie-telop-transcriber-win-x64-v0.1.2.zip -WhatIf` | `LaunchShortcutPath` が出力され、インストーラ実行ディレクトリに `.lnk` を作る想定を確認。 |
| 起動ショートカット作成 | `temp\issue127-smoke3\launch-root` からインストーラを実行 | `launch-root\Movie Telop Transcriber.lnk` が生成され、リンク先が `launch-root\MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` であることを確認。 |
| install manifest 記録 | 導入後 `movie-telop-transcriber.installation.json` を確認 | `installInvocationDirectory`、`launchShortcutPath`、`removeOcrRuntimeRootOnUninstall=true` を確認。 |
| アンインストール削除確認 | 更新済みアンインストーラを導入先へ上書きして実行し、15 秒待機後に残存確認 | 導入先、起動ショートカット、外部 `ocr-runtime`、スタートメニューショートカットの残存なしを確認。 |

## 4. 検証コマンド
```powershell
dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64

powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools\install\Install-MovieTelopTranscriber.ps1 `
  -PackageZipPath dist\movie-telop-transcriber-win-x64-v0.1.2.zip `
  -WhatIf

powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools\install\Install-MovieTelopTranscriber.ps1 `
  -PackageZipPath dist\movie-telop-transcriber-win-x64-v0.1.2.zip `
  -InstallRoot temp\issue127-smoke3\launch-root\MovieTelopTranscriber `
  -OcrRuntimeRoot temp\issue127-smoke3\ocr-runtime `
  -SkipModelDownload `
  -Force

powershell -NoProfile -ExecutionPolicy Bypass `
  -File temp\issue127-smoke3\launch-root\MovieTelopTranscriber\Uninstall-MovieTelopTranscriber.ps1
```

## 5. 結果
- 次回配布物へ入れるべき改善 2) の起動ショートカット作成は実装済み。
- 次回配布物へ入れるべき改善 3) のアンインストール削除漏れは、導入先、起動ショートカット、外部 `ocr-runtime`、スタートメニューショートカットまで削除されることを確認した。
- この検証は、現行 `v0.1.2` zip に同梱されている旧アンインストーラではなく、リポジトリ側の更新済みアンインストーラを導入先へ反映して実施した。したがって、利用者へ配るには次回パッチ release で再パッケージが必要である。
