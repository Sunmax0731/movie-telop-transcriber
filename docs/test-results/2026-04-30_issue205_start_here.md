# #205 配布物の START_HERE 導線追加

## 1. 実施内容
- `docs/templates/release_package_start_here.md` を追加し、release package の zip 直下へ `START_HERE.md` として同梱するようにした。
- `tools/release/New-ReleasePackage.ps1` の必須ファイル検証へ `START_HERE.md` を追加した。
- `README.md`、`docs/12_導入手順書.md`、`docs/18_配布物manifest.md` に、配布 zip 展開直後の入口として `START_HERE.md` を追記した。

## 2. 想定導線
1. zip を展開する
2. `START_HERE.md` を開く
3. `Install-MovieTelopTranscriber.cmd` または `Install-MovieTelopTranscriber.ps1` へ進む
4. 導入後に OCR readiness とサンプル解析を確認する
