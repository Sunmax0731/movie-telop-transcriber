# #51 配布物作成と GitHub Release 公開 検証結果

## 1. 検証情報
- 対象 Issue: `#51`
- 検証日: 2026-04-29
- 検証者: Codex
- 対象バージョン: `v0.1.0`

## 2. 対象
- `tools/release/New-ReleasePackage.ps1`
- `dist/movie-telop-transcriber-win-x64-v0.1.0.zip`
- `dist/movie-telop-transcriber-win-x64-v0.1.0.zip.sha256`

## 3. 配布物作成コマンド
```powershell
tools\release\New-ReleasePackage.ps1 -Version 0.1.0
```

## 4. 追加検証コマンド
```powershell
Expand-Archive -LiteralPath dist\movie-telop-transcriber-win-x64-v0.1.0.zip -DestinationPath temp\release-verify-v0.1.0 -Force
Get-FileHash -LiteralPath dist\movie-telop-transcriber-win-x64-v0.1.0.zip -Algorithm SHA256
```

## 5. 確認観点
- Release build が成功すること。
- アプリ本体 zip に `app/MovieTelopTranscriber.App.exe` が含まれること。
- アプリ本体 zip に `app/tools/ocr/paddle_ocr_worker.py` が含まれること。
- アプリ本体 zip に `docs/12_導入手順書.md` と `docs/13_リリースノート.md` が含まれること。
- アプリ本体 zip に `samples/basic_telop/sample_basic_telop.mp4` と `ground_truth.json` が含まれること。
- アプリ本体 zip に `test-data/basic_telop/botirist.mp4` が含まれないこと。
- `*.pdb` がアプリ本体 zip に含まれないこと。
- SHA-256 checksum を生成すること。

## 6. 結果
- Release build: 成功。
- zip 作成: 成功。
- checksum 生成: 成功。
- 展開後必須ファイル検査: 成功。
- `test-data/basic_telop/botirist.mp4` の非同梱確認: 成功。
- `*.pdb` 非同梱確認: 成功。

## 7. 配布物情報
| 項目 | 値 |
| --- | --- |
| zip | `dist/movie-telop-transcriber-win-x64-v0.1.0.zip` |
| checksum | `dist/movie-telop-transcriber-win-x64-v0.1.0.zip.sha256` |
| 展開後ファイル数 | 1,096 |
| 展開後サイズ | 634,670,703 bytes |
| zip サイズ | 246,779,347 bytes |
| SHA-256 | `021800d51d3af388862c5ca0a7ed2349781b05eeac0e2bac121554f44e43b372` |
