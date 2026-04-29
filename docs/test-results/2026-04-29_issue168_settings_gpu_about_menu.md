# Issue #168 設定画面サイズ再調整と GPU / ライセンス / バージョン導線追加

## 1. 変更概要
- 設定画面の初期サイズを `1480 x 620` に再調整した。
- 右ペインの最小幅を `720 -> 730` に拡大した。
- 処理状況ペインの最小高さを `150 -> 160` に拡大した。
- 設定画面に `PaddleOCR device` の選択 UI を追加し、`cpu` / `gpu:0` を選べるようにした。
- `ヘルプ` メニューを追加し、`ライセンス` と `バージョン情報` のダイアログを開けるようにした。

## 2. 検証
| 項目 | コマンド / 操作 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build .\\src\\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors |
| diff 整合性 | `git diff --check` | エラーなし |
| 起動確認 | Release build の `MovieTelopTranscriber.App.exe` を起動 | 成功。プロセスが 5 秒後も存続 |

## 3. 起動確認対象
- `D:\\Claude\\Movie\\movie-telop-transcriber\\src\\MovieTelopTranscriber.App\\bin\\x64\\Release\\net10.0-windows10.0.26100.0\\MovieTelopTranscriber.App.exe`

## 4. 補足
- `PaddleOCR device` を `gpu:0` にすると worker 数の選択 UI が有効化される。
- `cpu` 選択時は worker 数を `1` に正規化する。
- ライセンス画面は、同梱または設定対象の主要コンポーネント一覧と、OCR runtime のライセンス配置先の目安を表示する。
