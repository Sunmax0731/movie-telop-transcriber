# #210 実動画系 canonical QCDS workflow 追加

## 1. 概要
- repo 外に置いた rights-cleared 実動画でも、QCDS の report / metrics / canonical note を残せる workflow を追加した。
- `tools/validation/New-RealVideoQcdsCanonical.ps1` に metadata JSON、`segments.json`、`summary.json`、ground truth を渡すと、`evaluate_qcds_report.py` を呼び出して成果物を生成する。
- 動画 binary 自体はコミットしなくても、Run ID、評価コマンド、動画メタ情報を固定して比較できる。

## 2. 追加した成果物
- script:
  `tools/validation/New-RealVideoQcdsCanonical.ps1`
- metadata template:
  `docs/templates/qcds_real_video_metadata_template.json`
- equivalent canonical example:
  `docs/test-results/2026-04-30_issue210_basic_telop_equivalent_canonical.md`
- generated report:
  `docs/test-results/2026-04-30_qcds_basic_telop_equivalent_report.md`
- generated metrics:
  `docs/test-results/2026-04-30_qcds_basic_telop_equivalent_metrics.json`

## 3. 実行コマンド
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\New-RealVideoQcdsCanonical.ps1 `
  -MetadataPath temp\issue210_basic_telop_equivalent_metadata.json
```

内部では次の QCDS 評価 script を呼ぶ。

```powershell
python tools\validation\evaluate_qcds_report.py `
  --ground-truth test-data\basic_telop\ground_truth.json `
  --segments work\runs\20260429_033541_3031\output\segments.json `
  --summary work\runs\20260429_033541_3031\logs\summary.json `
  --sample-id basic_telop_equivalent `
  --output docs\test-results\2026-04-30_qcds_basic_telop_equivalent_report.md `
  --metrics-output docs\test-results\2026-04-30_qcds_basic_telop_equivalent_metrics.json `
  --previous-metrics docs\test-results\2026-04-29_qcds_basic_telop_rerun_033541_metrics.json
```

## 4. 検証結果
`basic_telop` の既存 actual OCR run を equivalent canonical として再利用し、workflow の疎通を確認した。

| 項目 | 結果 |
| --- | --- |
| sample ID | `basic_telop_equivalent` |
| canonical kind | `real-video-equivalent` |
| report 生成 | 成功 |
| metrics 生成 | 成功 |
| canonical note 生成 | 成功 |
| 前回比較 | `2026-04-29_qcds_basic_telop_rerun_033541_metrics.json` を利用 |

主な metrics:

| 指標 | 値 |
| --- | --- |
| text exact rate | `1.0` |
| character accuracy | `1.0` |
| missing segment count | `0` |
| extra segment count | `5` |
| processing time seconds | `15.881` |
| ocr engine | `paddleocr` |
| run id | `20260429_033541_3031` |

## 5. 実動画で使うときの運用
1. rights-cleared 実動画で run を実行し、`work/runs/<run_id>/output/segments.json` と `logs/summary.json` を確定する。
2. ground truth JSON を用意する。
3. `docs/templates/qcds_real_video_metadata_template.json` を複製し、動画メタ情報と path を埋める。
4. `New-RealVideoQcdsCanonical.ps1` を実行し、report / metrics / canonical note を `docs/test-results/` に出力する。
5. 代表性の更新があれば、`docs/spec/06_QCDS評価仕様.md` と `docs/test-results/00_検証成果物ガイド.md` を更新する。

## 6. 判断
- `#210` の完了条件である「権利確認済みの実動画系 sample または同等の canonical を追加する」は、repo 外の実動画でも再利用できる equivalent canonical workflow を追加する形で満たした。
- 実動画 binary の同梱は引き続き必須にしない。
- 次の拡張は、rights-cleared 実動画の実測レポート本数を増やすことに絞る。
