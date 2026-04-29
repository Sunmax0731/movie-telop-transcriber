# Issue 158 GPU 環境での OCR 2 worker 再評価

## 概要
- 対象 Issue: `#158`
- 計測日: 2026-04-29
- 目的: GPU 環境で `paddleocr` の 2 worker 並列を再評価し、設定項目として公開する価値があるか判断する。

## 環境
- GPU: `NVIDIA GeForce RTX 2070 SUPER`
- driver: `591.59`
- NVIDIA 表示上の CUDA version: `13.1`
- Python: `3.10`
- PaddlePaddle: `paddlepaddle-gpu==3.2.2`
- PaddleOCR: `3.5.0`
- paddlex: `3.5.1`
- device: `gpu:0`

## インストール条件
- GPU 用の検証は CPU 用 venv と分離し、`temp/ocr-eval-gpu/.venv` を新規作成した。
- PaddlePaddle の GPU wheel は公式 Windows pip ドキュメントに従い `cu126` 向けの `paddlepaddle-gpu==3.2.2` を使用した。
- 検証用 benchmark は `tools/validation/OcrWorkerModeBenchmark` を拡張し、以下を記録するようにした。
  - `MOVIE_TELOP_PADDLEOCR_DEVICE`
  - 実行 worker 数
  - warmup 時間
  - OCR wall-clock
  - `nvidia-smi` による GPU utilization / VRAM 使用量サンプル

## 計測対象
1. `test-data/basic_telop/botirist.mp4`
   - 先頭 40 フレーム
   - `#152` の CPU 評価と比較しやすい条件
2. `test-data/benchmark_suite/sample_basic_telop_60s.mp4`
   - 先頭 60 フレーム
   - 現行 benchmark suite の中尺サンプル

## 計測結果

### 1. `botirist.mp4` 40 フレーム
| モード | worker 数 | wall-clock ms | warmup 合計 ms | worker execution 合計 ms | GPU util max | GPU util avg | VRAM max MiB | error |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 単一 worker | 1 | 6,533.0 | 12,303.5 | 6,261.3 | 43 | 17.4 | 5,760 | 0 |
| 2 workers | 2 | 4,115.8 | 24,455.2 | 7,914.3 | 74 | 22.8 | 6,851 | 0 |

- wall-clock は `6.53s -> 4.12s` で約 `37.0%` 短縮
- warmup は `12.30s -> 24.46s` で約 `99%` 増加
- VRAM peak は `5,760 MiB -> 6,851 MiB` で約 `+1.1 GiB`

### 2. `sample_basic_telop_60s.mp4` 60 フレーム
| モード | worker 数 | wall-clock ms | warmup 合計 ms | worker execution 合計 ms | GPU util max | GPU util avg | VRAM max MiB | error |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 単一 worker | 1 | 7,647.9 | 11,598.4 | 7,144.1 | 51 | 21.3 | 5,109 | 0 |
| 2 workers | 2 | 5,728.2 | 22,934.8 | 10,758.3 | 86 | 31.6 | 6,246 | 0 |

- wall-clock は `7.65s -> 5.73s` で約 `25.1%` 短縮
- warmup は `11.60s -> 22.93s` で約 `97.7%` 増加
- VRAM peak は `5,109 MiB -> 6,246 MiB` で約 `+1.1 GiB`

## CPU 評価との比較
- `#152` の CPU 評価では `botirist.mp4` 40 フレームで `73.3s -> 62.1s` の約 `15.4%` 短縮だった。
- 同条件の GPU 評価では `6.53s -> 4.12s` の約 `37.0%` 短縮となり、2 worker の改善幅が明確に大きい。
- 一方で GPU でも worker execution 合計と warmup は増加するため、「常に得」ではない。

## 観察
1. GPU 環境では 2 workers の wall-clock 改善が CPU より大きい。
2. VRAM は 2 workers で約 1.1 GiB 追加で必要になる。
3. RTX 2070 SUPER 8GB では 2 workers が成立したが、常駐アプリ込みで `6.2GB - 6.9GB` 付近まで上がる。
4. GPU utilization は 2 workers の方が高く、単一 worker より GPU を使い切れている。
5. warmup は worker 数にほぼ比例して重くなり、単発実行では体感改善を打ち消す可能性がある。

## 判断
- GPU 環境では 2 workers 並列は採用価値がある。
- ただし既定値として常時有効にするのは避ける。
  - GPU 非搭載環境では意味がない
  - 8GB 級 GPU では VRAM 余裕が大きくない
  - warmup が約 2 倍になる
- 結論として、`GPU 利用時のみ選べる任意設定` として検討するのが妥当。

## 次アクション案
1. アプリ設定に `OCR worker 数` を追加し、GPU 利用時のみ `1` / `2` を選べるようにする。
2. 既定値は `1` のままにし、ヘルプテキストで「GPU 環境では 2 が速い場合があるが VRAM 消費が増える」と明記する。
3. 起動時 warmup が支配的なため、設定化する場合は `#154` の前倒し warmup と組み合わせる前提で扱う。
