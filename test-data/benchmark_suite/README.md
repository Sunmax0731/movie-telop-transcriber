# benchmark_suite サンプル

## 目的
- OCR 性能比較に使う短尺・中尺・長尺の代表サンプルを、再配布可能な形で固定する。
- `#131`、`#150`、`#151`、`#154` などの速度改善 Issue で、同じ入力条件を使って前後比較できるようにする。

## 構成
- `benchmark_samples.json`
  - ベンチマークで使う canonical sample 一覧。
- `sample_basic_telop_60s.mp4`
  - `test-data/basic_telop/sample_basic_telop.mp4` を 12 回連結した中尺サンプル。
- `sample_basic_telop_180s.mp4`
  - `test-data/basic_telop/sample_basic_telop.mp4` を 36 回連結した長尺サンプル。
- `create_benchmark_samples.py`
  - 中尺 / 長尺サンプルを再生成するスクリプト。

## サンプル一覧
| 区分 | sample_id | 入力動画 | 長さ | 備考 |
| --- | --- | --- | ---: | --- |
| 短尺 | `benchmark_short` | `test-data/basic_telop/sample_basic_telop.mp4` | 5 秒 | 既存の配布可能サンプル |
| 中尺 | `benchmark_medium` | `test-data/benchmark_suite/sample_basic_telop_60s.mp4` | 60 秒 | 短尺を 12 回連結 |
| 長尺 | `benchmark_long` | `test-data/benchmark_suite/sample_basic_telop_180s.mp4` | 180 秒 | 短尺を 36 回連結 |

## 再生成
```powershell
py -3 test-data\benchmark_suite\create_benchmark_samples.py
```

## ベンチマーク実行
```powershell
dotnet run --project tools\validation\OcrPerformanceBenchmark\OcrPerformanceBenchmark.csproj -p:Platform=x64 -- `
  --catalog test-data\benchmark_suite\benchmark_samples.json `
  --output-json temp\benchmark-suite-results.json
```

## 注意
- この sample suite は再配布可能性を優先した synthetic benchmark 用であり、実動画固有の複雑さは限定的。
- 実運用寄りの追加比較が必要な場合は、別 Issue で権利確認済み動画または利用者持ち込み動画のレポートを追加する。
