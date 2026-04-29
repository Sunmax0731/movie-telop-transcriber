# 配布物 manifest

## 1. 目的
この文書は、利用者と release 担当者の両方が、配布 asset ごとの役割、同梱物、非同梱物、導入後の最小確認手順を一目で把握できるようにするための manifest である。

## 2. 対象バージョン
- 最新基準:
  `v0.1.4`
- 詳細検証:
  [test-results/2026-04-30_v0.1.4_release.md](test-results/2026-04-30_v0.1.4_release.md)

## 3. asset 一覧

| asset | 用途 | 利用者が直接使うか |
| --- | --- | --- |
| `movie-telop-transcriber-win-x64-v0.1.4.zip` | アプリ本体、同梱 docs、サンプル、installer をまとめた配布物 | はい |
| `movie-telop-transcriber-win-x64-v0.1.4.zip.sha256` | zip の整合確認 | 必要に応じて |
| `Install-MovieTelopTranscriber.ps1` | PowerShell から導入する代替入口 | 必要に応じて |
| `Install-MovieTelopTranscriber.ps1.sha256` | installer script の整合確認 | 必要に応じて |

## 4. zip に同梱するもの
- WinUI 3 アプリの Release build 一式
- `tools/ocr/paddle_ocr_worker.py`
- installer / uninstaller
- 利用者向け docs
- 最小サンプル

## 5. zip に同梱しないもの
- Python runtime
- PaddlePaddle / PaddleOCR の Python package
- PaddleOCR モデル本体

これらは installer または別手順で準備する。

## 6. 利用者向けの最小確認手順
1. zip を展開する
2. `Install-MovieTelopTranscriber.cmd` または `Install-MovieTelopTranscriber.ps1` を使って導入する
3. `MovieTelopTranscriber\app\MovieTelopTranscriber.App.exe` を起動する
4. 設定画面で OCR 設定が解決されていることを確認する
5. 短いサンプル動画で 1 回 `抽出` を実行する
6. `segments.json` と `segments.srt` が出力されることを確認する

## 7. release 担当者向けの最小確認手順
1. asset 一覧が揃っていることを確認する
2. checksum を記録する
3. `docs/12_導入手順書.md` と `docs/13_リリースノート.md` の導線が最新であることを確認する
4. [test-results/2026-04-30_v0.1.4_release.md](test-results/2026-04-30_v0.1.4_release.md) 相当の検証が最新状態であることを確認する

## 8. 関連文書
- [11_配布構成と同梱物.md](11_配布構成と同梱物.md)
- [12_導入手順書.md](12_導入手順書.md)
- [13_リリースノート.md](13_リリースノート.md)
