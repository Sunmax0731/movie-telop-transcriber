# qcds_suite サンプル

## 目的
`docs/spec/06_QCDS評価仕様.md` の代表動画条件のうち、低コントラスト、複数位置、短時間表示を継続比較できるようにするための synthetic サンプル群。

## ファイル
- `create_qcds_samples.py`
  動画と ground truth を再生成するスクリプト
- `samples.json`
  サンプル一覧と推奨フレーム間隔
- `sample_low_contrast_telop.mp4`
  低コントラスト検証用動画
- `low_contrast_telop_ground_truth.json`
  低コントラスト動画の正解データ
- `sample_multi_position_telop.mp4`
  複数位置テロップ検証用動画
- `multi_position_telop_ground_truth.json`
  複数位置動画の正解データ
- `sample_short_duration_telop.mp4`
  短時間表示検証用動画
- `short_duration_telop_ground_truth.json`
  短時間表示動画の正解データ

## 推奨確認手順
1. `python test-data/qcds_suite/create_qcds_samples.py` を実行して sample を再生成する。
2. 対象動画ごとに推奨フレーム間隔で app または検証補助スクリプトを実行する。
3. `tools/validation/evaluate_qcds_report.py` で `ground_truth.json` と `segments.json` を比較する。

## 補足
- ここで追加したサンプルは、評価代表性を広げることが主目的であり、権利確認の難しい実動画の代替として扱う。
- baseline report は fixture output からも再生成できる。actual OCR run の比較結果に差し替える場合でも、ground truth は共通で使う。
