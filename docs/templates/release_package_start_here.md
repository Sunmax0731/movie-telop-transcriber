# START_HERE

## 1. 最短の導入手順
1. この zip を展開する
2. 展開先で `Install-MovieTelopTranscriber.cmd` をダブルクリックする
3. 導入完了後、`MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` を起動する

## 2. PowerShell から導入する場合
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\Install-MovieTelopTranscriber.ps1
```

## 3. 導入後に確認すること
- `Test-MovieTelopTranscriberOcrReadiness.ps1` で OCR readiness が `ready` になる
- `MovieTelopTranscriber\app\movie-telop-transcriber.settings.json` が生成される
- サンプル動画で `解析` を 1 回実行し、`segments.json` と `segments.srt` が出力される

## 4. 詳細ガイド
- 導入手順:
  `docs\12_導入手順書.md`
- 利用ガイド:
  `docs\16_利用ガイド.md`
- 配布物 manifest:
  `docs\18_配布物manifest.md`

## 5. 補足
- OCR runtime、PaddleOCR package、モデル本体は zip に含めず、installer または手動導入で準備する
- `self-contained publish` は現時点の標準配布に含めない
