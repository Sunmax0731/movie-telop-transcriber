# #209 OCR runtime pack と補助配布物の再評価

## 1. 結論
- 現時点では、`PaddleOCR ランタイムパック` を GitHub Release の標準 asset としては採用しない。
- あわせて、標準 zip に加える補助配布物も増やさない。

## 2. 評価した案

| 案 | 内容 | 判断 |
| --- | --- | --- |
| 標準 asset として runtime pack を追加 | Python 仮想環境、Python package、PaddleOCR モデルを別 zip 化する | 不採用 |
| 必要時のみ補助 asset として追加 | オフライン導入が必須な現場向けに別 asset を用意する | 保留 |
| 現行の installer / 手動導入を維持 | 標準配布は app zip + installer、閉域端末は手動導入 | 採用 |

## 3. 不採用理由
- 配布サイズが大きく、標準 release の asset 数と管理負荷が増える
- Python 仮想環境は Windows / Python / package の組み合わせに依存し、長期互換性の検証が追加で必要になる
- モデル取得キャッシュや `.paddlex` 配置を含めた再現条件が増え、検証 matrix が広がる
- 現時点では `Install-MovieTelopTranscriber.ps1` と `START_HERE.md`、`手動導入 / オフライン導入` の組み合わせで導入経路は成立している

## 4. 将来採用する条件
- 閉域端末や再配布用途で、runtime pack が実運用上必要だと確認できる
- asset の checksum、導入後 readiness、オフライン検証手順を release 前 smoke と同等に固定できる
- Python / package / model の組み合わせを release ごとに再現確認できる

## 5. 現在の方針
- 標準配布は `movie-telop-transcriber-win-x64-v<version>.zip` と `Install-MovieTelopTranscriber.ps1` を維持する
- オフライン運用は `docs/12_導入手順書.md` の手動導入 / オフライン導入を使う
- runtime pack は「将来候補」としてのみ残し、次の release asset には含めない
