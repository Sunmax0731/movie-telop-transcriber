# 配布物 manifest

## 1. 目的
利用者と release 判定者が、公開 asset の構成、含めるもの、含めないもの、導入前の確認項目を一目で把握できるようにする。

## 2. 対象バージョン
- 最新版:
  `v1.1.0`
- 検証レポート:
  [test-results/2026-04-30_v1.1.0_release.md](test-results/2026-04-30_v1.1.0_release.md)

## 3. asset 一覧

| asset | 用途 | 公開対象 |
| --- | --- | --- |
| `movie-telop-transcriber-win-x64-v1.1.0.zip` | アプリ本体、同梱 docs、サンプル、installer をまとめた配布物 | はい |
| `movie-telop-transcriber-win-x64-v1.1.0.zip.sha256` | zip の整合確認 | はい |
| `Install-MovieTelopTranscriber.ps1` | PowerShell から導入する代替入口 | はい |
| `Install-MovieTelopTranscriber.ps1.sha256` | installer script の整合確認 | はい |

## 4. zip に含めるもの
- WinUI 3 アプリの Release build 一式
- `tools/ocr/paddle_ocr_worker.py`
- installer / uninstaller
- 導入用 docs
- 最新サンプル

## 5. zip に含めないもの
- Python runtime
- PaddlePaddle / PaddleOCR の Python package
- PaddleOCR モデル本体

これらは installer または手動導入で取得する。`self-contained publish` は 2026-04-30 時点で起動失敗の既知制約があるため、現在の標準配布には含めない。

## 6. 導入前の最小確認項目
1. zip を展開する
2. `Install-MovieTelopTranscriber.cmd` または `Install-MovieTelopTranscriber.ps1` を使って導入する
3. `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` が存在することを確認する
4. 導入後に OCR readiness が `ready` になることを確認する
5. 代表サンプルで 1 本 `解析` を実行する
6. `segments.json` と `segments.srt` が出力されることを確認する

## 7. release 判定前の最小確認項目
1. asset 一覧が揃っていることを確認する
2. checksum を検証する
3. `docs/12_導入手順書.md` と `docs/13_リリースノート.md` が最新版であることを確認する
4. [test-results/2026-04-30_v1.1.0_release.md](test-results/2026-04-30_v1.1.0_release.md) 相当の検証が最新状態であることを確認する

## 8. 関連文書
- [11_配布構成と同梱物.md](11_配布構成と同梱物.md)
- [12_導入手順書.md](12_導入手順書.md)
- [13_リリースノート.md](13_リリースノート.md)
