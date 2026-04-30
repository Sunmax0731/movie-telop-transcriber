# 配布物 manifest

## 1. 目的
この文書は release asset の構成、zip に含めるもの、含めないもの、公開前の確認項目を一箇所で管理するための manifest である。

## 2. 対象 version
- 最新公開 release:
  `v1.2.0`
- 次回 release 対象:
  `v1.2.1`

## 3. v1.2.1 asset 一覧
| asset | 用途 | 公開対象 |
| --- | --- | --- |
| `movie-telop-transcriber-win-x64-v1.2.1.zip` | アプリ本体、同梱 docs、samples、installer をまとめた配布物 | はい |
| `movie-telop-transcriber-win-x64-v1.2.1.zip.sha256` | zip の整合確認 | はい |
| `Install-MovieTelopTranscriber.ps1` | PowerShell から直接実行する installer script | はい |
| `Install-MovieTelopTranscriber.ps1.sha256` | installer script の整合確認 | はい |
| `START_HERE.md` | zip 展開直後の導入案内 | zip 内に同梱 |

## 4. zip に含めるもの
- WinUI 3 アプリの Release build 一式
- `tools/ocr/paddle_ocr_worker.py`
- installer / uninstaller
- 導入手順と release 関連 docs
- samples

## 5. zip に含めないもの
- Python runtime
- PaddlePaddle / PaddleOCR の Python package
- PaddleOCR モデル本体

これらは installer または手動導入で構築する。`self-contained publish` は標準配布経路ではないため、現行 release asset には含めない。

## 6. 公開前の確認項目
1. zip 展開後に `Install-MovieTelopTranscriber.cmd` または `Install-MovieTelopTranscriber.ps1` で導入できる
2. `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` が起動する
3. OCR readiness が `ready` になる
4. `segments.json` と `segments.srt` を出力できる
5. Python 3.12 と Python 3.13 の両方で installer 完走を確認する
6. `docs/12_導入手順書.md`、`docs/13_リリースノート.md`、関連 test-results が最新状態である

## 7. 関連文書
- [11_配布構成と同梱物.md](11_配布構成と同梱物.md)
- [12_導入手順書.md](12_導入手順書.md)
- [13_リリースノート.md](13_リリースノート.md)
- [test-results/2026-04-30_issue213_store_python312_313_installer.md](test-results/2026-04-30_issue213_store_python312_313_installer.md)
- [test-results/2026-04-30_v1.2.1_release.md](test-results/2026-04-30_v1.2.1_release.md)
