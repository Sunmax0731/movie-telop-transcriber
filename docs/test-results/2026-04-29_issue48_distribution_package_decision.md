# #48 配布構成と同梱物の確定結果

## 1. 文書情報
- 対象 Issue: `#48`
- 実施日: 2026-04-29
- 対象工程: リリース
- 記録者: Codex

## 2. 確認対象
- GitHub Issue `#48`、親 Issue `#18`
- `README.md`
- `docs/02_開発工程.md`
- `docs/08_既知不具合と制約一覧.md`
- `docs/10_PaddleOCRワーカー導入手順.md`
- `docs/test-results/2026-04-28_オフライン導入実行検証.md`
- `docs/test-results/2026-04-28_PaddleOCRワーカー検証.md`
- `src/MovieTelopTranscriber.App/MovieTelopTranscriber.App.csproj`

## 3. 判断
- 初期リリースのアプリ本体配布は、Release build 出力を基準にする。
- self-contained publish 出力は #46 の起動失敗結果を引き継ぎ、初期リリースの配布候補にしない。
- PaddleOCR は実動画 OCR の既定エンジンとする。
- アプリ本体 zip には `tools/ocr/paddle_ocr_worker.py` を同梱する。
- Python runtime、PaddlePaddle / PaddleOCR の Python package、PaddleOCR モデル本体はアプリ本体 zip に同梱しない。
- オフライン導入時の Python 環境とモデル事前配置は #49 の導入手順で扱う。

## 4. 配布サイズ確認
2026-04-29 時点のローカル Release build 出力を確認した。

| 対象 | ファイル数 | 展開サイズ |
| --- | ---: | ---: |
| アプリ本体 Release build 出力 | 1084 | 605.18 MB |
| `tools/ocr/paddle_ocr_worker.py` | 1 | 16,470 bytes |
| Windows OCR fallback worker の Release build 出力 | 7 | 25.83 MB |

## 5. 更新した文書
- `docs/11_配布構成と同梱物.md`
- `README.md`
- `docs/02_開発工程.md`
- `docs/08_既知不具合と制約一覧.md`
- `docs/10_PaddleOCRワーカー導入手順.md`

## 6. #49 への引き継ぎ
- Release build 出力を `app/` 配下へ配置する手順を記載する。
- `.runtimeconfig.json` が参照する .NET 10 runtime の導入前提を確認手順に含める。
- PaddleOCR Python 環境、Python package、モデルのオンライン導入とオフライン事前配置を分けて記載する。
- `MOVIE_TELOP_PADDLEOCR_PYTHON` と `MOVIE_TELOP_PADDLEOCR_DEVICE=cpu` を明示する。
- サンプル動画を使った初回確認手順を記載する。
