# #138 ベンチマークサンプル整備

## 1. 対象
- Issue: `#138`
- ブランチ: `issue-138-benchmark-sample-kit`
- 実施日: 2026-04-29
- 実施者: Codex

## 2. 対応内容
- `test-data/benchmark_suite/` を追加し、短尺・中尺・長尺の canonical benchmark sample を定義した。
- 中尺 `sample_basic_telop_60s.mp4` を `temp/issue131-generated/` から repo 配下へ昇格した。
- 長尺は権利不明のローカル動画に依存しないよう、短尺サンプルを 36 回連結した `sample_basic_telop_180s.mp4` を新設した。
- `benchmark_samples.json` で sample ID、長さ、入力パス、生成元、repeat 回数を管理するようにした。
- `create_benchmark_samples.py` で中尺 / 長尺サンプルを再生成できるようにした。
- `tools/validation/OcrPerformanceBenchmark` を追加し、catalog を入力に 3 サンプルを連続計測できるようにした。

## 3. canonical sample
| 区分 | sample_id | 入力動画 | 長さ | 備考 |
| --- | --- | --- | ---: | --- |
| 短尺 | `benchmark_short` | `test-data/basic_telop/sample_basic_telop.mp4` | 5 秒 | 既存の配布可能サンプル |
| 中尺 | `benchmark_medium` | `test-data/benchmark_suite/sample_basic_telop_60s.mp4` | 60 秒 | 短尺を 12 回連結 |
| 長尺 | `benchmark_long` | `test-data/benchmark_suite/sample_basic_telop_180s.mp4` | 180 秒 | 短尺を 36 回連結 |

## 4. baseline
- baseline の計測観点は、総待ち時間だけでなく次も保持する。
  - frame extraction
  - OCR total
  - worker initialization
  - worker execution
  - attribute analysis
  - attribute write
  - first / average / max frame
- 既存の初回 baseline は [2026-04-29_issue131_ocr_performance_benchmark.md](/abs/path/d:/Claude/Movie/movie-telop-transcriber/docs/test-results/2026-04-29_issue131_ocr_performance_benchmark.md) を参照する。
- 今後は `tools/validation/OcrPerformanceBenchmark` を使って同じ観点で再実行する。

### 4.1 2026-04-29 baseline
`tools/validation/OcrPerformanceBenchmark` で `benchmark_samples.json` を実行し、次の baseline を取得した。

| 区分 | フレーム数 | OCR 実行 | OCR 再利用 | frame extraction ms | OCR total ms | warmup ms | total wait ms | average frame ms | max frame ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 短尺 | 5 | 3 | 2 | 71.8 | 5,675.1 | 8,948.6 | 14,637.4 | 1,130.5 | 2,132.4 |
| 中尺 | 60 | 25 | 35 | 501.7 | 44,199.4 | 9,956.9 | 54,159.0 | 734.2 | 3,266.7 |
| 長尺 | 180 | 73 | 107 | 2,649.1 | 133,782.6 | 11,736.6 | 145,522.0 | 740.6 | 3,720.4 |

ログ:
- 短尺: `temp/ocr-performance-benchmark-runs/20260429_215100_6609/logs/summary.json`
- 中尺: `temp/ocr-performance-benchmark-runs/20260429_215114_a4bf/logs/summary.json`
- 長尺: `temp/ocr-performance-benchmark-runs/20260429_215209_ff01/logs/summary.json`

## 5. 実行コマンド
### サンプル再生成
```powershell
py -3 test-data\benchmark_suite\create_benchmark_samples.py
```

### ベンチマーク
```powershell
dotnet run --project tools\validation\OcrPerformanceBenchmark\OcrPerformanceBenchmark.csproj -p:Platform=x64 -- `
  --catalog test-data\benchmark_suite\benchmark_samples.json `
  --output-json temp\benchmark-suite-results.json
```

## 6. 確認
- `sample_basic_telop_60s.mp4` と `sample_basic_telop_180s.mp4` が `test-data/benchmark_suite/` に存在することを確認した。
- `py -3 test-data\benchmark_suite\create_benchmark_samples.py` が成功することを確認した。
- `dotnet tools\validation\OcrPerformanceBenchmark\bin\x64\Release\net10.0-windows10.0.26100.0\OcrPerformanceBenchmark.dll --catalog test-data\benchmark_suite\benchmark_samples.json --output-json temp\issue138-benchmark-suite-results.json` が成功することを確認した。
- `dotnet build tools\validation\OcrPerformanceBenchmark\OcrPerformanceBenchmark.csproj -c Release -p:Platform=x64` が成功することを確認した。
- `git diff --check` が通ることを確認した。

## 7. 結論
- 3 種類の benchmark sample が repo 配下で再生成可能になり、計測条件も `catalog + tool` として固定できた。
- これにより、速度改善 Issue の前後比較を同じ入力条件で再実行できる状態になった。
