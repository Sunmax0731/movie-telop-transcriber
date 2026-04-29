# #122 EXE 直起動と settings.json 導入導線 実装検証

## 1. 対象
- Issue: `#122` ダブルクリックで導入開始と起動ができる入口を追加する
- 実施日: 2026-04-29
- 実施者: Codex

## 2. 実装概要
- 起動時に `app\movie-telop-transcriber.settings.json` を読み込み、OCR engine、PaddleOCR 用 Python、worker script、主要 OCR 設定を反映する層を追加した。
- インストーラは `movie-telop-transcriber.settings.json` を生成し、スタートメニューは `MovieTelopTranscriber.App.exe` 直起動へ寄せた。
- 配布物には `Install-MovieTelopTranscriber.cmd` を同梱し、利用者が PowerShell コマンドを手入力しなくても導入を開始できるようにした。

## 3. 確認項目
| 項目 | コマンド / 方法 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功 |
| インストーラ計画確認 | `Install-MovieTelopTranscriber.ps1 -WhatIf` | 成功。`AppSettingsPath` が出力されることを確認 |
| 配布物再生成 | `tools\release\New-ReleasePackage.ps1 -Version 0.1.1 -SkipBuild` | 成功。zip を再生成 |
| ローカル導入 | `Install-MovieTelopTranscriber.ps1 -PackageZipPath ... -InstallRoot ... -OcrRuntimeRoot ... -SkipOcrSetup -SkipModelDownload -NoStartMenuShortcut -Force` | 成功。`movie-telop-transcriber.settings.json` を生成 |
| 設定ファイル内容 | `Get-Content app\movie-telop-transcriber.settings.json` | 成功。`ocrEngine=paddleocr`、`pythonPath`、`scriptPath`、`device=cpu`、前処理設定を確認 |
| EXE 直起動 | 導入先 `app\MovieTelopTranscriber.App.exe` を起動し 5 秒後にプロセス生存確認 | 成功。起動直後に異常終了しないことを確認 |

## 4. 生成された設定ファイル例
```json
{
  "ocrEngine": "paddleocr",
  "paddleOcr": {
    "pythonPath": "<InstallRoot>\\..\\ocr-runtime\\.venv\\Scripts\\python.exe",
    "scriptPath": "<InstallRoot>\\app\\tools\\ocr\\paddle_ocr_worker.py",
    "device": "cpu",
    "language": "ja",
    "minScore": 0.5,
    "normalizeSmallKana": true,
    "preprocess": true,
    "contrast": 1.1,
    "sharpen": true
  }
}
```

## 5. 判断
- 利用者は配布物の `Install-MovieTelopTranscriber.cmd` から導入を開始できる。
- 導入後は `MovieTelopTranscriber.App.exe` をダブルクリックして起動できる。
- OCR 実行時の設定解決は `settings.json` 側へ寄せられ、PowerShell 起動スクリプトは互換用の位置づけになった。

## 6. 残課題
- `SkipOcrSetup` 指定時は `pythonPath` が未生成の仮想環境を指すため、実動画 OCR 実行前に OCR runtime 導入が別途必要。
- 将来的に利用者が設定画面から OCR runtime の場所を変更する要件が出る場合は、`settings.json` の再生成または GUI 編集機能を別 Issue で扱う。
