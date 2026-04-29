# self-contained publish 再現条件固定化

## 1. 基本情報
- 対象 Issue: `#194`
- 実施日: 2026-04-30
- 対象: self-contained publish の再現条件と検証手順
- 記録者: Codex

## 2. 目的
`dotnet publish --self-contained true` の失敗を、毎回同じ入口から再現確認できるようにする。

この文書は、self-contained 改善バックログの canonical 手順として扱う。

## 3. canonical 手順

### 3.1 publish と起動確認をまとめて実行する

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-SelfContainedPublish.ps1 `
  -Configuration Release `
  -Platform x64 `
  -RuntimeIdentifier win-x64 `
  -OutputDirectory .\temp\self-contained-repro `
  -WaitSeconds 30
```

### 3.2 期待する確認観点
- publish が成功するか
- `MovieTelopTranscriber.App.exe` が生成されるか
- 指定秒数待機後にプロセスが残っているか
- 終了していた場合の exit code は何か
- 出力ファイル数と合計サイズはどれくらいか

## 4. 2026-04-30 実測結果

実行結果:
- publish は成功
- 出力先: `temp\self-contained-repro`
- `PublishedFileCount`: `555`
- `PublishedTotalBytes`: `291,704,571`
- `WaitSeconds`: `30`
- `ProcessStatus`: `exited`
- `ExitCode`: `-1073741189`
- `ExitCodeHex`: `0xC000027B`

判断:
- 30 秒時点でプロセスは残存せず、従来と同じ `0xC000027B` 系で終了した
- self-contained publish は 2026-04-30 時点でも成立条件に使わない

## 5. 旧検証との差分

| 項目 | 2026-04-28 | 2026-04-30 |
| --- | --- | --- |
| publish 出力先 | `temp\offline-publish` | `temp\self-contained-repro` |
| 実行入口 | 手動コマンド列 | `Test-SelfContainedPublish.ps1` |
| 起動確認 | 起動直後終了の確認 | 30 秒待機の明示確認 |
| 終了コード | `0xC000027B` | `0xC000027B` |

今回の更新点は、`手順の固定化` と `再実行入口の明示化` である。

## 6.1 補足
- publish 時に `IL2104` の trim warning が複数出るが、2026-04-30 時点ではまず起動失敗の再現確認を優先し、この Issue では warning の解消までは扱わない。

## 7. 次の比較検証への引き継ぎ
- `#187` では WinUI / Windows App SDK 最小構成との比較に進む
- `#195` では publish 条件の差分マトリクスを作る
- `#188` では self-contained 未成立を利用者が誤解しない診断導線を強化する
