# Windows OCR ワーカー検証結果

## 1. 文書情報
- 対象 Issue: `#72`
- 実施日: 2026-04-28
- 対象: `MovieTelopTranscriber.Ocr.Windows`
- 記録者: Codex

## 2. 実施内容
`sample_basic_telop.mp4` から抽出済みのフレームに対し、Windows OCR worker を単体実行した。

```powershell
dotnet build src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -p:Platform=x64
dotnet build src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -c Release -p:Platform=x64

dotnet run --project src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -p:Platform=x64 -- `
  work\runs\20260428_155035_e28c\ocr\ocr-000011-00001000ms.request.json `
  temp\windows-ocr-response-1000-filtered.json

src\MovieTelopTranscriber.Ocr.Windows\bin\x64\Release\net10.0-windows10.0.26100.0\MovieTelopTranscriber.Ocr.Windows.exe `
  work\runs\20260428_155035_e28c\ocr\ocr-000011-00001000ms.request.json `
  temp\windows-ocr-response-1000-release.json
```

## 3. 結果
- worker の終了コードは `0`。
- `status` は `success`。
- 00:00:01.000 のフレームからテロップ行を 1 件検出した。
- 小さい時刻表示行は、高さフィルターにより除外された。
- 検出テキストは `サンプ丿レテロップ` であり、期待値 `サンプルテロップ` と完全一致はしなかった。
- Debug / Release の worker 単体実行で同等の response JSON が生成された。

## 4. 判断
- sidecar なしの画像に対し、実 OCR worker 経由で文字行を検出する経路は成立した。
- Windows OCR baseline は追加ランタイムが少なく導入しやすい一方、日本語テロップの認識精度には制約が残る。
- 初期リリースで Windows OCR を採用する場合は、精度制約をリリースノートと導入手順へ明記する必要がある。

## 5. 後続確認
- アプリ本体を `MOVIE_TELOP_OCR_WORKER` 指定で起動し、抽出から出力まで Windows OCR worker で通ること。
- 実動画で代表的なテロップ文字が検出されること。
- Windows OCR の精度が不足する場合、PaddleOCR または Tesseract worker を再評価すること。
