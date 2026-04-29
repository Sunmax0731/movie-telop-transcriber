# Issue 133 文字色推定改善検証

## 概要
- 対象 Issue: `#133`
- 実施日: 2026-04-29
- 対象コード: `src/MovieTelopTranscriber.App/Services/TelopAttributeAnalysisService.cs`

## 変更方針
- OCR 矩形全体の色分布だけで文字色を決めると、背景帯や背景色の面積が大きいケースで誤判定しやすい。
- 今回は OCR 矩形の四隅を背景サンプルとして扱い、その色成分を矩形全体の集計から差し引いた前景集計を作る。
- 文字色は前景集計を優先して `白文字`、`黄文字`、`赤文字`、`青文字`、`緑文字`、`黒文字` の暫定ラベルへ分類する。
- 枠色は従来どおり OCR 矩形全体の黒/白画素量から `黒枠` または `白縁` を判定する。

## 確認方法
1. `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64`
2. `test-data/basic_telop/sample_basic_telop.mp4` の `1000ms` と `3000ms` のフレームを抽出する。
3. `test-data/basic_telop/expected_ocr_by_timestamp.json` の bounding box を使って `TelopAttributeAnalysisService` を実行する。

## 確認結果
| 時刻 | 想定ケース | 判定結果 |
| --- | --- | --- |
| `1000ms` | 青帯の上に白文字、黒枠 | `白文字 / 黒枠` |
| `3000ms` | 背景帯なしの黄文字、黒枠 | `黄文字 / 黒枠` |

## 所見
- 背景帯が大きい `1000ms` ケースでも、背景色の青に引っ張られず `白文字` を優先できた。
- `3000ms` ケースでは背景の暗色を差し引いた結果、`黄文字` を安定して抽出できた。
- `background_color` は引き続き `null` のままとし、今回の対応では文字色と枠色の暫定ラベル改善に範囲を限定した。
