# #102 OCR エンジン未設定時の誤起動防止 実装検証

## 1. 対象
- Issue: `#102` OCR エンジン未設定時の誤起動をアプリ側で防止する
- ブランチ: `issue-102-ocr-engine-guard`
- 実施日: 2026-04-29
- 記録者: Codex

## 2. 実装内容
- OCR エンジン未指定かつ外部 worker 未指定の場合、`OcrWorkerClientFactory` は `PaddleOcrWorkerClient` を選択する。
- `MOVIE_TELOP_OCR_ENGINE=json-sidecar` など PaddleOCR 以外を明示した場合、`ProcessOcrWorkerClient` の sidecar 検証経路を使う。
- sidecar 検証時にフレーム同名の `.ocr.json` がない場合、`ProcessOcrWorkerClient` は `OCR_SIDECAR_NOT_FOUND` の `error` 応答を保存する。
- OCR エラーが返った場合、メイン画面の処理状況と直近失敗表示に OCR エラー内容を表示する。
- 設定説明、README、設計、仕様、テスト計画、既知制約を実装後の OCR 選択規則へ更新した。

## 3. 検証コマンド
```powershell
dotnet run --project temp\Issue102OcrGuardSmoke\Issue102OcrGuardSmoke.csproj -p:Platform=x64
dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64
git diff --check
```

## 4. 検証結果
### 4.1 OCR ガード smoke
一時プロジェクト `temp/Issue102OcrGuardSmoke` で以下を確認した。

```text
default_engine=paddleocr
sidecar_engine=json-sidecar
sidecar_status=error
sidecar_error=OCR_SIDECAR_NOT_FOUND
```

判定:
- OCR エンジン未指定かつ外部 worker 未指定時の既定は `paddleocr`。
- `json-sidecar` 明示時に sidecar がない場合は `OCR_SIDECAR_NOT_FOUND` の OCR エラーになる。
- sidecar なし実動画が空検出の正常完了に見える経路は防止されている。

### 4.2 ビルド
`dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64` は成功した。

```text
ビルドに成功しました。
    0 個の警告
    0 エラー
```

### 4.3 ドキュメント整合
- `README.md` の現在 Issue を `#102` に更新した。
- `docs/02_開発工程.md`、`docs/05_詳細設計書.md`、`docs/06_テスト計画書.md`、`docs/07_実装メモ.md`、`docs/08_既知不具合と制約一覧.md`、`docs/10_PaddleOCRワーカー導入手順.md` を更新した。
- `docs/spec/03_OCRとテロップ属性仕様.md` と `docs/design/04_処理パイプライン詳細設計.md` を更新した。
- ルートワークスペースの `AGENTS.md` / `SKILL.md` も、現行実装では未指定時に PaddleOCR を既定とする前提へ更新した。

## 5. 残事項
- PaddleOCR Python ランタイム、モデル同梱、配布構成は `#48` / `#49` で確定する。
- 実動画 OCR 精度と速度の最終調整は、`#72` の残確認およびリリース工程で継続する。
