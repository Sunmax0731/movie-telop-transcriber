# Issue #114 README 再構成・導入インストーラ検証

## 1. 文書情報
- 対象 Issue: `#114`
- 実施日: 2026-04-29
- 対象工程: リリース後改善
- 記録者: Codex

## 2. 対象変更
- `README.md` を利用者向け / 開発者向けの二部構成に再構成した。
- `tools/install/Install-MovieTelopTranscriber.ps1` を追加した。
- `tools/ocr/paddle_ocr_worker.py` に `--warmup-models` を追加した。
- `docs/10_PaddleOCRワーカー導入手順.md`、`docs/11_配布構成と同梱物.md`、`docs/12_導入手順書.md` を更新した。
- `tools/release/New-ReleasePackage.ps1` でインストーラを zip 内と `dist/` の個別 asset 候補へ出力するようにした。

## 3. 検証結果
| 項目 | コマンド | 結果 |
| --- | --- | --- |
| インストーラ計画出力 | `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install\Install-MovieTelopTranscriber.ps1 -WhatIf` | 成功。既定の導入先、Release asset URL、OCR runtime 先、起動スクリプト先を確認 |
| PaddleOCR worker CLI | `py -3.10 .\tools\ocr\paddle_ocr_worker.py --help` | 成功。`--warmup-models`、`--warmup-language`、`--warmup-image` が表示されることを確認 |
| Python 構文確認 | `py -3.10 -m py_compile .\tools\ocr\paddle_ocr_worker.py` | 成功 |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。警告 0、エラー 0 |
| 差分検査 | `git diff --check` | 成功 |
| 禁止表現確認 | `rg` による対象語検索 | 該当なし |
| 配布物作成 | `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\New-ReleasePackage.ps1 -Version 0.1.0 -SkipBuild` | 成功。zip、zip checksum、インストーラ、インストーラ checksum を生成 |
| ローカル zip 導入確認 | `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install\Install-MovieTelopTranscriber.ps1 -PackageZipPath .\dist\movie-telop-transcriber-win-x64-v0.1.0.zip -InstallRoot .\temp\installer-smoke\MovieTelopTranscriber -OcrRuntimeRoot .\temp\installer-smoke\ocr-runtime -SkipOcrSetup -SkipModelDownload -NoStartMenuShortcut -Force` | 成功。アプリ配置、worker 配置、起動スクリプト生成を確認 |

## 4. 配布物作成結果
`New-ReleasePackage.ps1` の出力:

| 項目 | 値 |
| --- | --- |
| PackageName | `movie-telop-transcriber-win-x64-v0.1.0` |
| ZipPath | `dist\movie-telop-transcriber-win-x64-v0.1.0.zip` |
| ChecksumPath | `dist\movie-telop-transcriber-win-x64-v0.1.0.zip.sha256` |
| InstallerPath | `dist\Install-MovieTelopTranscriber.ps1` |
| InstallerChecksumPath | `dist\Install-MovieTelopTranscriber.ps1.sha256` |
| FileCount | `1097` |
| ExpandedSizeBytes | `634690138` |
| ZipSizeBytes | `246784687` |

## 5. 未実施範囲
- 検証では実ネットワーク経由の PaddleOCR package 導入とモデル取得は実行していない。
- `--warmup-models` は CLI 表示と Python 構文を確認し、実モデル取得はインストーラの実運用時に実行する前提とした。
- スタートメニューショートカット作成は、検証環境を汚さないため `-NoStartMenuShortcut` で省略した。
