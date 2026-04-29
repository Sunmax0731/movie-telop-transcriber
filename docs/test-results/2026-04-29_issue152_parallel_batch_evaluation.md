# Issue 152 OCR worker 並列化 / バッチ化評価

## 概要
- 対象 Issue: `#152`
- 計測日: 2026-04-29
- 目的: OCR worker の並列化またはバッチ化が、長尺ケースの総待ち時間短縮に有効かを評価する。

## 評価対象
1. 単一 worker 直列実行
2. 2 workers 並列実行
3. 3 workers 並列実行
4. バッチ化可否の実装観点レビュー

## 計測条件
- 動画: `test-data/basic_telop/botirist.mp4`
- 対象フレーム: 先頭 40 フレーム
- OCR エンジン: `paddleocr`
- device: `cpu`
- language: `ja`
- preprocess: `true`
- contrast: `1.1`
- sharpen: `true`
- 各 worker は warmup 済みで計測

## 計測結果
| モード | worker 数 | wall-clock ms | worker execution 合計 ms | 平均フレーム ms | 最大フレーム ms | error |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 単一 worker 直列 | 1 | 73,332.3 | 72,915.6 | 1,833.3 | 4,070.8 | 0 |
| 2 workers 並列 | 2 | 62,067.7 | 122,790.0 | 3,076.5 | 5,052.9 | 0 |
| 3 workers 並列 | 3 | 92,446.8 | 273,643.6 | 6,850.2 | 11,478.1 | 0 |

## 観察
1. 2 workers 並列は wall-clock を約 15.4% 短縮した
   - `73.3s -> 62.1s`
   - ただし worker execution 合計は大きく増えており、CPU 資源を強く取り合っている
2. 3 workers 並列は明確に悪化した
   - wall-clock も平均フレーム時間も増加
   - CPU 前提では過並列になっている
3. スパイクは 2 workers でも残る
   - 最大フレーム時間は `4.1s -> 5.1s` とむしろ少し悪化
4. warmup コストも worker 数に比例して増えやすい
   - 2 workers: `22.3s`
   - 3 workers: `60.5s`

## バッチ化の可否
- 現行 `tools/ocr/paddle_ocr_worker.py` は 1 回のコマンドで `request_path` と `response_path` を 1 つずつ受け取る設計
- `recognize()` も単一画像パスを前提に `ocr.predict()` を呼んでいる
- そのため、現行契約のままでは batch 実験はできない
- batch を試すには、少なくとも次が必要
  - request / response JSON 契約の拡張
  - stdio コマンド形式の変更
  - worker 側で複数画像をまとめて処理する実装
  - App 側で複数フレームを束ねる呼び出し経路

## 実装コストと期待効果
- 2 workers 並列:
  - 実装コスト: 中
  - 期待効果: CPU では限定的
  - リスク: CPU 使用率上昇、warmup 増加、スパイク残存
- 3 workers 以上:
  - 実装コスト: 中
  - 期待効果: CPU では低い
  - リスク: 悪化が見えているため採用非推奨
- バッチ化:
  - 実装コスト: 高
  - 期待効果: 未測定
  - リスク: 契約変更範囲が広く、今回の CPU-only 環境では有効性未確認

## 判断
- 現時点の既定環境が CPU である限り、複数 worker 並列をそのまま本実装へ入れる優先度は高くない。
- 2 workers 並列は一定の wall-clock 改善があるが、改善幅に対してコストと負荷増が重い。
- 3 workers 並列は採用しない。
- batch 化は別設計トラックとして扱うべきで、今回の Issue では採用判断に進めない。

## 次に試す方式
- GPU 環境で 2 workers 並列を再評価する。
- CPU 前提の既定経路では、`#151` の候補選別と `#154` の warmup 前倒しを維持する。
