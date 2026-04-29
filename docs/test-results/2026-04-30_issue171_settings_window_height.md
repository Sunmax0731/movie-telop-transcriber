# Issue #171 設定画面の高さ再調整

## 1. 変更概要
- 設定画面の初期サイズを `1480 x 620` から `1480 x 930` に変更した。

## 2. 検証
| 項目 | コマンド / 操作 | 結果 |
| --- | --- | --- |
| Release build | `dotnet build .\\src\\MovieTelopTranscriber.sln -c Release -p:Platform=x64` | 成功。0 warnings / 0 errors |
| diff 整合性 | `git diff --check` | エラーなし |
| 起動確認 | Release build の `MovieTelopTranscriber.App.exe` を起動 | 成功。プロセスが 5 秒後も存続 |

## 3. 起動確認対象
- `D:\\Claude\\Movie\\movie-telop-transcriber\\src\\MovieTelopTranscriber.App\\bin\\x64\\Release\\net10.0-windows10.0.26100.0\\MovieTelopTranscriber.App.exe`
