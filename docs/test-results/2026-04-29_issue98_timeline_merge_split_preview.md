# Issue #98 タイムライン結合・分割 UI とプレビュー同期検証

## 1. 文書情報
- 対象 Issue: `#98`
- 実施日: 2026-04-29
- 対象工程: リリース後改善
- 記録者: Codex

## 2. 対応内容
- タイムライン / 結果行に、代表 detection ID とは別に関連 detection ID 群を保持できるようにした。
- セグメント行のプレビュー強調は、単一 detection ID ではなく関連 detection ID 群を優先するようにした。
- 結合後のセグメントでは、結合後テキストに含まれる元 detection を関連 detection として扱う。
- 分割後のセグメントでは、分割後テキストを含む元 detection を関連 detection として扱う。
- 結合 / 分割ボタンを、編集可能なタイムライン行の操作として再表示した。
- 詳細設計、実装メモ、README を更新した。

## 3. 検証結果
| 項目 | コマンド | 結果 |
| --- | --- | --- |
| Release build | `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。警告 0、エラー 0 |
| 差分検査 | `git diff --check` | 成功 |
| 禁止表現確認 | `rg` による対象語検索 | 該当なし |

## 4. 手動未確認範囲
- GUI を起動した結合 / 分割ボタン操作、プレビュー overlay の目視位置確認はこの検証では未実施。
- `出力のみ` 実行後の `segments.json.edits` の目視確認は、この検証では未実施。

## 5. 残リスク
- 分割後の文字列が元 detection の一部に相当する場合、プレビュー上の強調枠は元 detection の bounding box になる。文字列単位で bounding box を再計算する処理は現時点では行わない。
- より細かい分割位置指定 UI が必要な場合は、別 Issue として分割ダイアログを設計する。
