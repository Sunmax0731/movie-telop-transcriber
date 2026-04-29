# MainPageViewModel 責務分割方針

## 1. 目的
この文書は、`MainPageViewModel` に集中している責務を、段階的に分割する方針を記録する。

## 2. 現状の責務塊
`MainPageViewModel` は、主に次の責務を持っている。

1. 画面状態の保持
2. PaddleOCR 設定の読込、環境変数反映、永続化
3. 動画読み込み、フレーム抽出、OCR 実行、出力再実行の制御
4. タイムライン編集
5. プレビュー同期
6. プロジェクト保存 / 再読込

## 3. 今回の分割方針
2026-04-30 時点では、まず `2. PaddleOCR 設定の読込、環境変数反映、永続化` を `MainPageUserSettingsCoordinator` へ切り出す。

理由:
- 環境変数名、既定値、正規化、保存処理が `MainPageViewModel` に集まりやすい
- 画面状態保持と比べて入出力境界が比較的明確で、段階分割しやすい
- OCR 設定変更は今後も継続して増えるため、先に独立させる効果が大きい

## 4. 分割後の役割

### 4.1 MainPageViewModel に残すもの
- ObservableProperty による画面状態保持
- 画面イベントからのコマンド起動
- タイムライン選択とプレビュー選択の同期
- 保存 / 読込後に UI へ反映する最終判断

### 4.2 MainPageUserSettingsCoordinator に寄せるもの
- PaddleOCR 設定の既定値
- 環境変数名の定義
- 保存済み UI 設定の復元補正
- project bundle 内 UI 設定の復元補正
- PaddleOCR 設定の環境変数反映
- launch settings への永続化
- worker count と device の正規化

## 5. 次段階候補
次の分割候補は、以下の順で検討する。

1. プロジェクト保存 / 再読込
2. タイムライン編集
3. プレビュー同期
4. 解析実行フロー制御

2026-04-30 追記:
- `ProjectLoadSaveCoordinator` を追加し、保存入力の組み立てと読込結果の UI 反映用スナップショット生成を `MainPageViewModel` から切り出した

## 6. 完了条件との対応
- 責務分割方針の文書化:
  この文書で対応する
- 少なくとも 1 つの大きな責務塊を別クラスへ切り出す:
  `MainPageUserSettingsCoordinator` を追加して対応する
- 依存関係と public surface の見直し:
  `MainPageViewModel` から設定保存ロジックと環境変数操作を外す
- 分割後も build と主要操作確認を通す:
  build / test / issue コメントで記録する
