# Issue 150 PaddleOCR warmup 先行実行検証

## 概要
- 対象 Issue: `#150`
- 計測日: 2026-04-29
- 目的: PaddleOCR worker の warmup を先行実行し、初回フレーム待ち時間と体感待ち時間がどう変化するか確認する。

## 実装内容
- `IOcrWorkerClient.WarmupAsync` を追加した。
- `PaddleOcrWorkerClient` は OCR 開始前に `warmup.ppm` を使った先行認識を実行する。
- `MainPageViewModel` は OCR 開始前に warmup を呼び、進捗欄へ `OCR warmup` を表示する。
- `summary.json` / `run.log` に `ocr_warmup_status` と `ocr_warmup_ms` を追加した。

## 計測条件
- OCR エンジン: `paddleocr`
- device: `cpu`
- language: `ja`
- preprocess: `true`
- contrast: `1.1`
- sharpen: `true`
- frame interval: `1.0 sec`

## 比較対象
- 改善前 baseline: [2026-04-29_issue131_ocr_performance_benchmark.md](/abs/path/d:/Claude/Movie/movie-telop-transcriber/docs/test-results/2026-04-29_issue131_ocr_performance_benchmark.md)
- 改善後: 本 Issue の warmup 実装入り branch で短尺・中尺を再計測

## 比較結果
| 区分 | 改善前 初回フレーム ms | 改善後 warmup ms | 改善後 初回フレーム ms | 改善率 | 改善前 OCR 合計 ms | 改善後 OCR 合計 ms | warmup + OCR 合計 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 短尺 | 32,476.9 | 16,920.1 | 3,673.1 | -88.7% | 44,321.5 | 15,539.3 | 32,459.4 |
| 中尺 | 14,810.5 | 17,071.4 | 3,456.9 | -76.7% | 194,143.5 | 206,137.6 | 223,209.0 |

## 観察
1. 初回フレーム待ち時間は大きく短縮した
   - 短尺は `32.5s -> 3.7s`
   - 中尺は `14.8s -> 3.5s`
2. warmup 自体は約 17 秒かかった
   - モデル初期化と初回推論コストの大半は warmup 側へ移った
3. 総待ち時間は一貫して短縮しなかった
   - 短尺は `warmup + OCR` 合計でも baseline より短かった
   - 中尺は `warmup + OCR` 合計が baseline より長かった
4. 体感面では改善がある
   - OCR 開始後に長時間無反応に見える状態を避けやすくなった
   - 進捗欄で `OCR warmup` を明示できるようになった

## 判断
- 本実装は「最初のフレームが極端に遅い」問題には有効。
- 一方で、warmup を OCR 直前にそのまま追加すると、総待ち時間の短縮は保証されない。
- 現段階では、体感改善と進捗の可視化を優先した実装としては成立する。
- 総待ち時間まで安定して削るには、warmup をフレーム抽出中や動画選択直後へ前倒しして重ねる追加改善が必要。
