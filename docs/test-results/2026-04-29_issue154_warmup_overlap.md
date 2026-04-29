# Issue 154 OCR warmup 前倒し検証

## 概要
- 対象 Issue: `#154`
- 計測日: 2026-04-29
- 目的: OCR warmup をフレーム抽出前後へ前倒しし、総待ち時間をどこまで隠蔽できるか確認する。

## 実装内容
- 動画メタデータ読込後に PaddleOCR warmup をバックグラウンド開始する。
- フレーム抽出中は warmup を並行実行する。
- OCR 開始時は、未完了なら warmup 完了だけを待ってから OCR 本処理へ進む。
- warmup 用ファイルは一時ディレクトリで処理し、完了後に削除する。

## 比較対象
- baseline 1: `#131` の warmup なし
- baseline 2: `#150` の OCR 直前 warmup
- 今回: metadata 読込後に background warmup を開始し、フレーム抽出と重ねる

## 計測条件
- OCR エンジン: `paddleocr`
- device: `cpu`
- language: `ja`
- preprocess: `true`
- contrast: `1.1`
- sharpen: `true`
- frame interval: `1.0 sec`

## 比較結果
| 区分 | #150 直列 warmup 合計 ms | #154 前倒し合計 ms | 差分 | 改善率 | #154 初回フレーム ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 短尺 | 32,561.9 | 29,254.6 | -3,307.3 | -10.2% | 3,127.3 |
| 中尺 | 223,878.3 | 181,079.1 | -42,799.2 | -19.1% | 2,699.7 |

計算方法:
- `#150 直列 warmup 合計 ms = frame_extraction_ms + ocr_warmup_ms + ocr_total_ms`
- `#154 前倒し合計 ms = metadata 読込直後から OCR 完了までの実測 wall-clock`

## 観察
1. `#150` より総待ち時間は短縮した
   - warmup を抽出と重ねることで、待機時間の一部を隠蔽できた。
2. 初回フレーム待ち時間も引き続き抑えられている
   - 短尺 `3.1s`
   - 中尺 `2.7s`
3. 抽出時間が短い動画では改善幅は限定的
   - 短尺では抽出が約 `0.1s` のため、前倒しの主な効果は「動画選択後の待機中」に依存する。
4. 中尺では総待ち時間の改善が確認できた
   - 抽出時間と warmup を重ねたぶん、直列実行より差が出た。

## 進捗表示方針
- 動画選択後: `Metadata loaded. OCR warmup is running in background.`
- OCR 開始時:
  - warmup 完了済みならそのまま OCR 進行へ入る
  - 未完了なら `OCR warmup: preparing worker.` を表示して待つ
  - warmup 失敗時は `OCR warmup failed. Continuing with extracted frames.` として本処理へ進む

## 判断
- `#150` の直列 warmup より、前倒しのほうが総待ち時間を下げやすい。
- 特に利用者が動画選択後すぐに開始しない運用では、体感待ち時間の隠蔽効果が大きい。
- 直後実行ケースでも一定の改善があるため、本方式は採用してよい。
