# Windows OCR ワーカー導入手順

## 1. 文書情報
- 対象 Issue: `#72`
- 作成日: 2026-04-28
- 対象工程: リリース
- 記録者: Codex

## 2. 目的
`MOVIE_TELOP_OCR_WORKER` に指定できる実 OCR worker として、Windows 標準 OCR を利用する `MovieTelopTranscriber.Ocr.Windows` の使い方を定義する。

## 3. 位置付け
- `MovieTelopTranscriber.Ocr.Windows` は、初期リリースで sidecar なしの実画像 OCR を確認するための baseline worker とする。
- OCR エンジンは Windows の `Windows.Media.Ocr` を利用する。
- 追加の Python ランタイムや外部 OCR モデルは不要。
- 認識品質は Windows OCR とインストール済み言語に依存するため、PaddleOCR などの専用 OCR より高精度とは限らない。
- 2026-04-28 の手動検証では日本語テロップの認識精度が不足したため、#72 では PaddleOCR PP-OCRv5 worker を採用し、本 worker は fallback として扱う。

## 4. ビルド
```powershell
dotnet build src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -p:Platform=x64
```

出力例:

```text
src\MovieTelopTranscriber.Ocr.Windows\bin\x64\Debug\net10.0-windows10.0.26100.0\MovieTelopTranscriber.Ocr.Windows.exe
```

Release 用:

```powershell
dotnet build src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -c Release -p:Platform=x64
```

## 5. アプリから利用する方法
アプリ起動前に、OCR worker の実行ファイルを環境変数へ設定する。

```powershell
$env:MOVIE_TELOP_OCR_WORKER = "D:\Claude\Movie\movie-telop-transcriber\src\MovieTelopTranscriber.Ocr.Windows\bin\x64\Debug\net10.0-windows10.0.26100.0\MovieTelopTranscriber.Ocr.Windows.exe"
$env:MOVIE_TELOP_OCR_ENGINE = "windows-ocr"

Start-Process `
  -FilePath "D:\Claude\Movie\movie-telop-transcriber\src\MovieTelopTranscriber.App\bin\x64\Release\net10.0-windows10.0.26100.0\MovieTelopTranscriber.App.exe" `
  -WorkingDirectory "D:\Claude\Movie\movie-telop-transcriber\src\MovieTelopTranscriber.App\bin\x64\Release\net10.0-windows10.0.26100.0"
```

## 6. Worker 単体実行
アプリ本体が生成した OCR request JSON と response JSON の出力先を指定して実行できる。

```powershell
dotnet run --project src\MovieTelopTranscriber.Ocr.Windows\MovieTelopTranscriber.Ocr.Windows.csproj -p:Platform=x64 -- `
  work\runs\<run_id>\ocr\ocr-000011-00001000ms.request.json `
  temp\windows-ocr-response.json
```

## 7. 設定
### 7.1 OCR worker
| 環境変数 | 内容 |
| --- | --- |
| `MOVIE_TELOP_OCR_WORKER` | `MovieTelopTranscriber.Ocr.Windows.exe` のパス |
| `MOVIE_TELOP_OCR_ENGINE` | 画面とログに表示するエンジン名。推奨値は `windows-ocr` |

### 7.2 小さい文字行の除外
Windows OCR は、動画上部の時刻表示なども文字として検出する場合がある。`MovieTelopTranscriber.Ocr.Windows` は、既定で高さ 18px 未満の行を除外する。

```powershell
$env:MOVIE_TELOP_WINDOWS_OCR_MIN_HEIGHT = "18"
```

## 8. 既知制約
- Windows OCR の利用可否と言語対応は、利用環境にインストールされている Windows OCR 言語に依存する。
- 日本語テロップは検出できても、文字列が完全一致しない場合がある。
- 現時点では Windows OCR の行単位検出結果をそのまま共通 OCR 契約へ変換する。
- 信頼度は Windows OCR API から取得できないため `null` として出力する。
- 小さい文字行除外はヒューリスティックであり、実動画によって調整が必要になる可能性がある。
- 共通出力属性では、`font_size` は OCR 矩形高さ相当、色/枠は暫定ラベル、`font_family` と `background_color` は `null` 許容として扱う。

## 9. リリース工程への引き継ぎ
- #72 では、Windows OCR worker を fallback として残しつつ、PaddleOCR PP-OCRv5 worker で実動画 OCR を成立させる。
- #48 では、初期リリースのアプリ本体 zip には Windows OCR worker exe を既定同梱しない方針にした。必要な場合は fallback 用の別手順または別配布物として扱う。
- #49 では、本手順の扱いを `docs/12_導入手順書.md` へ統合済み。
- PaddleOCR 採用後に配布形態が重くなりすぎる場合は、Tesseract または ONNX Runtime 利用を代替候補として再評価する。
