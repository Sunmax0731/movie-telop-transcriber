# #89 SRT / VTT / ASS 字幕出力 実装検証

## 対象
- Issue: `#89`
- 実施日: 2026-04-29
- 実施者: Codex
- 対象ブランチ: `issue-89-subtitle-output`

## 実装対象
- `segments` から `segments.srt` を出力する。
- `segments` から `segments.vtt` を出力する。
- `segments` から初期標準スタイルの `segments.ass` を出力する。
- `summary.json` と `run.log` に SRT / VTT / ASS のパスを記録する。
- 設定画面の現在設定サマリに `JSON / CSV / SRT / VTT / ASS` の出力形式を表示する。

## 自動検証
実行コマンド 1:

```powershell
dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64
```

結果 1:
- 成功
- 警告 0
- エラー 0

実行コマンド 2:

```powershell
dotnet run --project temp\issue89-export-smoke\issue89-export-smoke.csproj
```

結果 2:
- 成功
- `temp\issue89-export-smoke-run\output` に JSON / CSV / SRT / VTT / ASS が生成された。
- SRT、VTT、ASS の基本ヘッダーと時刻形式を確認した。

## 手動確認観点
- OCR 実行後、`output` ディレクトリに `segments.srt`、`segments.vtt`、`segments.ass` が生成されること。
- SRT が連番、`HH:MM:SS,mmm --> HH:MM:SS,mmm`、テキスト、空行のブロックになっていること。
- VTT の先頭が `WEBVTT` で、時刻が `HH:MM:SS.mmm` 形式になっていること。
- ASS に `[Script Info]`、`[V4+ Styles]`、`[Events]`、`Dialogue` 行が含まれること。
- `logs/summary.json` と `logs/run.log` に `srt_path`、`vtt_path`、`ass_path` が記録されること。
- 設定画面の現在設定サマリに `JSON / CSV / SRT / VTT / ASS` が表示されること。

## 判断
- SRT / VTT は字幕ツール連携用の初期出力として今回のリリース範囲に含める。
- ASS は標準スタイルのみの初期対応とし、テロップ属性に基づく詳細スタイル反映は #75 の属性整理後に再評価する。
