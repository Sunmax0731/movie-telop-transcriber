# #85 手動確認中の PaddleOCR worker 起動失敗調査

## 対象 run
- `work/runs/20260428_205846_db10`
- 入力動画: `test-data/basic_telop/botirist.mp4`
- OCR エンジン: `paddleocr`

## 画面状況
- 結果一覧は全フレームで `PADDLEOCR_WORKER_STOPPED` を表示。
- run log は `frame_count=185`、`detection_count=0`、`segment_count=0`、`error_count=185`。

## 確認したログ
- `work/runs/20260428_205846_db10/logs/run.log`
- `work/runs/20260428_205846_db10/logs/summary.json`
- `work/runs/20260428_205846_db10/ocr/ocr-000001-00000000ms.response.json`

## 原因
`ocr-000001-00000000ms.response.json` の `error.details` に以下が記録されていた。

```text
ModuleNotFoundError: No module named 'paddleocr'
```

アプリ起動時に `MOVIE_TELOP_OCR_ENGINE=paddleocr` は設定されていたが、`MOVIE_TELOP_PADDLEOCR_PYTHON` が未指定だったため、既定の `python` が使われた。この Python には PaddleOCR が導入されていない。

## 対応
- `MOVIE_TELOP_PADDLEOCR_PYTHON` に `temp/ocr-eval/.venv/Scripts/python.exe` を指定して起動する。
- 拡大率は `1.0` 固定にし、設定画面の拡大率入力 UI を削除する。
