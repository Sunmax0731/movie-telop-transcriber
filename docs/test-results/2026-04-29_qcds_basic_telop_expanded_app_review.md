# QCDS拡張評価レポート: basic_telop

## 1. 文書情報
- 記録 Issue: `#118`
- 実施日: 2026-04-29
- 評価対象 run: `20260429_033541_3031`
- 入力動画: `test-data/basic_telop/sample_basic_telop.mp4`
- OCR エンジン: `paddleocr`
- 評価者: Codex

## 2. 評価範囲
このレポートは、`docs/spec/06_QCDS評価仕様.md`、`docs/templates/qcds_evaluation_report_template.md`、`docs/06_テスト計画書.md` にある観点を統合し、最新 run の成果物を再評価したものである。

今回の再評価はファイル成果物、ログ、既存検証記録、Release build の自動検証を対象にした。GUI を起動した目視確認、マウス操作、クリップボード操作、プレビュー overlay の画面上の位置確認は実施していない。

## 3. 入力と成果物
| 項目 | 値 |
| --- | --- |
| Run ID | `20260429_033541_3031` |
| status | `success` |
| OCR engine | `paddleocr` |
| 入力動画長 | 5,000 ms |
| フレーム数 | 5 |
| OCR 検出数 | 9 |
| 出力セグメント数 | 7 |
| 警告 / エラー | 0 / 0 |
| 処理時間 | 15.881 sec |

生成済み成果物:
- `output/segments.json`
- `output/segments.csv`
- `output/frames.csv`
- `output/segments.srt`
- `output/segments.vtt`
- `output/segments.ass`
- `logs/run.log`
- `logs/summary.json`

## 4. Q: Quality
| 観点 | 評価 | 根拠 | メモ |
| --- | --- | --- | --- |
| 正解テロップ文字列 | 合格 | 正解 2 件が完全一致。文字単位一致率も 100.0%。 | `サンプルテロップ`、`重要なお知らせ` は一致。 |
| 欠落 | 合格 | 欠落セグメント数 0。 | 正解 2 件はどちらもマッチした。 |
| 余計な検出 | 要改善 | 余計な検出セグメント数 5。 | `00.0s` から `04.0s` の動画内時刻表示が残っている。 |
| セグメント統合 | 一部合格 | 正解テロップは 1-3 sec、3-5 sec で統合済み。 | 時刻表示もセグメント化されているため、除外ルールが必要。 |
| 小書き仮名 | 今回未評価 | `basic_telop` 正解データに小書き仮名ケースがない。 | 別検証では PaddleOCR worker の補正を確認済みだが、この run の QCDS 代表動画では未測定。 |
| 低コントラスト | 今回未評価 | `basic_telop` では低コントラスト専用ケースがない。 | 追加サンプルが必要。 |
| 複数位置テロップ | 今回未評価 | 正解セグメントは 2 件で、位置差分の正解評価がない。 | 複数位置の順序や overlay 同期は手動確認対象。 |
| 短時間表示 | 一部合格 | 1 秒抽出間隔で 2 秒表示の正解 2 件は時刻一致。 | 1 秒未満または 1 フレームだけの短時間表示は未評価。 |
| 属性ラベル | リリース範囲内で合格 | `text_color`、`stroke_color`、`text_type` は暫定ラベルとして出力。 | 厳密な色値、フォント名、背景帯は初期リリース範囲外。 |

## 5. C: Cost
| 観点 | 評価 | 根拠 | メモ |
| --- | --- | --- | --- |
| 処理時間 | 注意 | 5 秒動画に対し 15.881 秒。 | CPU PaddleOCR 前提では約 3.18 倍の処理時間。動画長や設定に比例して増える可能性がある。 |
| 処理量 | 合格 | 5 フレーム、9 OCR 検出、7 セグメント。 | 評価用の最小サンプルとしては追跡可能な規模。 |
| Release build | 合格 | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` は警告 0 / エラー 0。 | 現行 main の build は成立。 |
| OCR 依存 | 注意 | PaddleOCR Python runtime、PaddlePaddle、PaddleOCR、モデルはアプリ本体 zip に非同梱。 | 導入コストは `docs/12_導入手順書.md` で扱う。 |
| 配布サイズ | 注意 | `v0.1.1` zip は約 246 MB、展開後は約 634 MB。 | WinUI / Windows App SDK / OpenCV / ONNX Runtime 依存が主因。 |

## 6. D: Delivery
| 観点 | 評価 | 根拠 | メモ |
| --- | --- | --- | --- |
| 開始時刻追跡 | 合格 | 平均開始時刻誤差 0 ms、最大 0 ms。 | 正解 2 件で一致。 |
| 終了時刻追跡 | 合格 | 平均終了時刻誤差 0 ms、最大 0 ms。 | 正解 2 件で一致。 |
| 出力ファイル生成 | 合格 | JSON、CSV、SRT、VTT、ASS、run log、summary が生成済み。 | `logs/summary.json` の各成果物パスと実ファイルが対応。 |
| 字幕出力 | 一部合格 | SRT / VTT / ASS は形式どおり生成。 | 余計な時刻表示も字幕に入るため、字幕利用時の品質には影響する。 |
| run 分離 | 合格 | `work/runs/20260429_033541_3031/` 配下に frames / ocr / attributes / output / logs が分離保存。 | 前回 run を上書きしない構成。 |
| 追跡可能性 | 合格 | `segments.json` に source video、processing settings、frames、segments、run metadata が含まれる。 | detection ID と frame image path で中間結果を追跡できる。 |
| application_version | 注意 | この run の `run_metadata.application_version` は `1.0.0.0`。 | run 作成時点がバージョン反映前のため。現行 `v0.1.1` の assembly metadata は `0.1.1`。 |

## 7. S: Sustainability / Satisfaction
| 観点 | 評価 | 根拠 | メモ |
| --- | --- | --- | --- |
| 警告 / エラー | 合格 | `warning_count=0`、`error_count=0`。 | run は `success`。 |
| GUI 主要操作 | 一部確認済み | `docs/test-results/2026-04-28_gui主要操作シナリオ.md` に動画選択、設定変更、解析開始、サブ画面操作の記録あり。 | その後の UI 改善後に目視再確認が必要な項目が残る。 |
| プレビュー overlay | 今回未確認 | ファイル成果物から矩形データと frame path は追跡可能。 | 画面上の枠位置ずれは GUI 起動が必要。 |
| タイムライン同期 | 今回未確認 | セグメント時刻と frame path は整合。 | 行選択とプレビュー同期の目視確認は未実施。 |
| タイムライン編集 | 既存実装あり、今回未操作 | README と #86 検証記録で編集、削除、edits 出力方針を確認。 | 今回 run では編集操作をしていないため、`edits` の実出力は未評価。 |
| 結合 / 分割 | 既知制約 | `#98` でリリース後対応。 | UI からは非表示として扱う。 |
| 導入継続性 | 合格 | `docs/12_導入手順書.md`、`docs/13_リリースノート.md`、GitHub Release `v0.1.1` が整備済み。 | PaddleOCR runtime は利用者環境側の前提。 |
| 保守性 | 合格 | QCDS script、metrics JSON、レポートテンプレート、release packaging script がある。 | 追加動画を入れれば比較評価を継続できる。 |

## 8. 代表動画条件の充足
| 条件 | 今回の充足 | 評価 |
| --- | --- | --- |
| 日本語横書きテロップ | あり | 合格 |
| 小書き仮名を含むテロップ | なし | 未評価 |
| 低コントラストテロップ | なし | 未評価 |
| 複数位置のテロップ | 正解データ上は不足 | 未評価 |
| 短時間表示テロップ | 2 秒表示はあり | 一部合格 |

## 9. 総合判定
`basic_telop` に対する文字列認識、時刻追跡、ファイル出力、ログ生成は合格とする。特に正解 2 セグメントは文字列・開始時刻・終了時刻がすべて一致している。

一方で、動画内の時刻表示が 5 件の余計なセグメントとして残っており、字幕出力にも混入している。実利用上は、時刻表示、UI 表示、固定位置の補助文字をテロップ本体から除外する後処理が次の改善候補になる。

また、今回の run はバージョン反映前に作成されているため、`run_metadata.application_version` は `1.0.0.0` のままである。`v0.1.1` の出力メタデータ確認は、現行 Release build で再実行した run を別途評価する必要がある。

## 10. 次に評価すべき項目
1. `v0.1.1` build で `basic_telop` を再実行し、`run_metadata.application_version=0.1.1` を確認する。
2. 時刻表示を除外するルールを設計し、extra segment count を 5 から 0 に下げられるか確認する。
3. 小書き仮名、低コントラスト、複数位置、1 秒未満表示を含む追加代表動画を用意する。
4. GUI を起動して、プレビュー overlay、タイムライン選択同期、コピー / 編集 / 削除、列表示 ON/OFF、シーケンスバーを目視確認する。
5. #98 で結合 / 分割 UI とプレビュー同期の再設計を進める。
