# #204 release smoke / 回帰確認の標準化

## 1. 概要
- `tools/validation/Test-ReleaseSmoke.ps1` を追加し、release 前の最小確認を 1 本の PowerShell から再実行できるようにした。
- この script は Release build、test、release package 作成、installer 実行、OCR readiness、canonical レポート存在確認を順に行い、結果を `temp/release-smoke/v<version>/release-smoke-summary.json` に残す。

## 2. 実行コマンド

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-ReleaseSmoke.ps1 `
  -Version 1.0.0
```

## 3. 確認した項目

| 項目 | 結果 | 補足 |
| --- | --- | --- |
| Release build | 成功 | `src\MovieTelopTranscriber.sln` を `Release/x64` で build |
| test | 成功 | `MovieTelopTranscriber.App.Tests` が 18 件成功 |
| release package | 成功 | `dist\movie-telop-transcriber-win-x64-v1.0.0.zip` と checksum を再生成 |
| installer 実行 | 成功 | `temp\release-smoke\v1-0-0\launch-root\MovieTelopTranscriber` へ導入 |
| OCR readiness | 成功 | `OcrReadinessStatus=ready` |
| canonical 参照確認 | 成功 | `basic_telop`、`issue203`、`issue207` のレポート存在を確認 |

## 4. 成果物
- script:
  `tools/validation/Test-ReleaseSmoke.ps1`
- 実行サマリ:
  `temp/release-smoke/v1-0-0/release-smoke-summary.json`

## 5. 使い方
- 次版 release 前は、対象 version を変えてこの script を実行し、結果 JSON と release レポートの両方を残す。
- build/test/package/install/readiness のいずれかが失敗した場合は、その時点で release 判定を止める。
