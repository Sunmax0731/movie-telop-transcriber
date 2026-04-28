# #75 テロップ属性リリース範囲整理

## 1. 対象
- Issue: `#75` テロップ属性推定の未対応項目をリリース範囲へ整理する
- ブランチ: `issue-75-attribute-release-scope`
- 実施日: 2026-04-29
- 記録者: Codex

## 2. 判断結果
初期リリースでは、OCR 結果の確認、編集、字幕出力を優先し、テロップ属性は補助情報として扱う。

| 項目 | 初期リリース判断 |
| --- | --- |
| `font_size` | OCR bounding box の高さ相当値として出力する。実フォントサイズではない。 |
| `text_color` | `白文字`、`黒文字`、`緑文字`、`黄文字`、`赤文字`、`青文字` などの暫定ラベルとして出力する。 |
| `stroke_color` | `黒枠`、`白枠` などの暫定ラベルとして出力する。 |
| `text_type` | 色/枠ラベルの結合、または `未分類` として出力する。意味分類ではない。 |
| `font_family` | 現行実装では `null` を許容する。 |
| `background_color` | 現行実装では `null` を許容する。 |
| SRT / VTT | テキストと時刻のみを出力する。 |
| ASS | 標準スタイル `Default` のみを出力し、属性はスタイルへ反映しない。 |

## 3. 反映先
- `docs/spec/07_テロップ属性リリース範囲.md`
- `docs/spec/03_OCRとテロップ属性仕様.md`
- `docs/spec/04_出力仕様.md`
- `docs/design/04_処理パイプライン詳細設計.md`
- `docs/05_詳細設計書.md`
- `docs/06_テスト計画書.md`
- `docs/07_実装メモ.md`
- `docs/08_既知不具合と制約一覧.md`
- `docs/09_Windows_OCRワーカー導入手順.md`
- `docs/10_PaddleOCRワーカー導入手順.md`
- `README.md`

## 4. 後続候補
| 優先度 | 項目 |
| --- | --- |
| P1 | 正確な文字色/枠色の代表色抽出 |
| P1 | 背景帯/地色の検出 |
| P2 | 見出し、人物名、通常テロップなどの意味分類 |
| P2 | ASS スタイル反映 |
| P3 | フォント名推定 |

## 5. 検証
- `dotnet build src\MovieTelopTranscriber.sln -p:Platform=x64`
- `python -m py_compile tools\validation\evaluate_basic_telop_accuracy.py`
- `git diff --check`

## 6. 判定
#75 の完了条件を満たす。

- 初期リリースでの属性出力範囲を README と仕様から判断できる。
- 未対応属性をバグではなく既知制約として説明した。
- 後続実装候補と優先度を明確にした。
