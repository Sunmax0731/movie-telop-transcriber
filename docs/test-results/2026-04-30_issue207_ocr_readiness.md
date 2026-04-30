# #207 installer 後の OCR readiness 診断

## 1. 概要
- installer 導入後に `Python / paddle / paddleocr / worker script / model cache` の成立を確認する `Test-MovieTelopTranscriberOcrReadiness.ps1` を追加した。
- installer は導入完了時にこの script を配置し、自動で readiness check を実行して `OcrReadinessStatus` を返すようにした。
- README と導入手順書に、手動再確認コマンドと切り分け観点を追記した。

## 2. 検証コマンド
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\install\Install-MovieTelopTranscriber.ps1 `
  -WhatIf

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\temp\issue207-install\MovieTelopTranscriber\Test-MovieTelopTranscriberOcrReadiness.ps1 `
  -InstallRoot .\temp\issue207-install\MovieTelopTranscriber `
  -AsJson

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\install\Install-MovieTelopTranscriber.ps1 `
  -InstallRoot .\temp\issue207-install\MovieTelopTranscriber `
  -OcrRuntimeRoot .\temp\issue207-install\ocr-runtime `
  -SkipAppInstall `
  -SkipOcrSetup `
  -Force
```

## 3. 検証結果
| 項目 | 結果 | 補足 |
| --- | --- | --- |
| `-WhatIf` 出力 | 成功 | `OcrReadinessScriptPath` が計画出力に含まれる |
| readiness script 単体実行 | 成功 | `status=ready`、`pythonVersion=3.10.6`、`paddle=3.2.0`、`paddleocr=3.5.0` |
| installer の自動 readiness 実行 | 成功 | `Running OCR readiness check` 後に `OCR readiness: ready` を出力 |
| installer の返り値 | 成功 | `OcrReadinessStatus=ready`、`OcrReadinessScriptPath=<InstallRoot>\Test-MovieTelopTranscriberOcrReadiness.ps1` |

## 4. 追加した導線
- install root に `Test-MovieTelopTranscriberOcrReadiness.ps1` を配置
- installer 完了時に readiness check を自動実行
- README に手動再確認コマンドとトラブルシュート入口を追加
- 導入手順書に `status=ready` の確認手順を追加

## 5. 補足
- readiness script は PowerShell 5 系で動くことを前提に、null 条件演算子や `python -c` の quoting に依存しない実装へ調整した。
- `SkipModelDownload` を使った場合は、model cache の有無に応じて `warning` になる可能性がある。オンライン導入の標準経路では `ready` を期待値とする。
