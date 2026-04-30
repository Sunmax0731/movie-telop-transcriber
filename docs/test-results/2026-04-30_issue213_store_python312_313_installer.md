# 2026-04-30 issue213 Store Python 3.12 / 3.13 installer 検証

## 1. 対象
- Issue:
  `#213`
- 対象 version:
  `v1.2.1`
- 目的:
  Store 配布を含む Python 3.12 / 3.13 で installer が完走し、OCR readiness が `ready` になることを確認する

## 2. 実装要点
- installer の Python 検出を `py -3.13`、`py -3.12`、`py -3.11`、`py -3.10`、`python`、`python3` に拡張
- `python.exe` フルパス指定時の空 `PythonArguments` を許容
- Python probe の `-c` コードを PowerShell 経由で壊れない形へ修正
- OCR runtime 依存を `paddlepaddle==3.2.2`、`paddleocr==3.5.0` とした

## 3. 検証環境
- OS:
  Windows
- package:
  `dist/movie-telop-transcriber-win-x64-v1.2.1.zip`
- Python 3.12:
  `C:\Users\gkkjh\AppData\Roaming\uv\python\cpython-3.12-windows-x86_64-none\python.exe`
- Python 3.13:
  `C:\Users\gkkjh\AppData\Roaming\uv\python\cpython-3.13-windows-x86_64-none\python.exe`

## 4. 検証コマンド
### 4.1 Python 3.12
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\install\Install-MovieTelopTranscriber.ps1 `
  -Version 1.2.1 `
  -PackageZipPath .\dist\movie-telop-transcriber-win-x64-v1.2.1.zip `
  -DownloadRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py312\download `
  -InstallRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py312\install `
  -OcrRuntimeRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py312\ocr-runtime `
  -PythonCommand C:\Users\gkkjh\AppData\Roaming\uv\python\cpython-3.12-windows-x86_64-none\python.exe `
  -Force
```

### 4.2 Python 3.13
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\install\Install-MovieTelopTranscriber.ps1 `
  -Version 1.2.1 `
  -PackageZipPath .\dist\movie-telop-transcriber-win-x64-v1.2.1.zip `
  -DownloadRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py313\download `
  -InstallRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py313\install `
  -OcrRuntimeRoot D:\Claude\Movie\movie-telop-transcriber\temp\issue213-py313\ocr-runtime `
  -PythonCommand C:\Users\gkkjh\AppData\Roaming\uv\python\cpython-3.13-windows-x86_64-none\python.exe `
  -Force
```

## 5. 結果
| 項目 | Python 3.12 | Python 3.13 |
| --- | --- | --- |
| Python version 判定 | 成功 | 成功 |
| 64-bit 判定 | 成功 | 成功 |
| `venv` 作成 | 成功 | 成功 |
| `paddlepaddle==3.2.2` 導入 | 成功 | 成功 |
| `paddleocr==3.5.0` 導入 | 成功 | 成功 |
| `--warmup-models` | 成功 | 成功 |
| OCR readiness | `ready` | `ready` |

## 6. 補足
- 途中で `paddlepaddle==3.3.1` を試したが、Windows CPU warmup で oneDNN/PIR 系の `NotImplementedError` により失敗した
- 公式 issue / discussion でも `3.3.x` 系の同系統不具合と `3.2.2` への切り戻しが確認できたため、release 依存は `3.2.2` を採用した
