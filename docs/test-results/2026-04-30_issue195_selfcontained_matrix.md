# issue #195 self-contained publish 条件の検証マトリクス

## 1. 概要
- 目的:
  self-contained publish の成立条件を、publish オプション差分ごとに比較する
- 実施日:
  2026-04-30
- 対象:
  `src/MovieTelopTranscriber.App/MovieTelopTranscriber.App.csproj`

## 2. 比較軸
- `Configuration`
- `PublishTrimmed`
- `PublishReadyToRun`
- `WindowsAppSdkDeploymentManagerInitialize`

## 3. シナリオ
- `release-baseline`
  現行 Release 相当
- `release-no-trim`
  trim 無効
- `release-no-r2r`
  ReadyToRun 無効
- `release-init-enabled`
  deployment manager initialize を有効化
- `debug-baseline`
  Debug 相当

## 4. 実測結果

| Scenario | Configuration | PublishTrimmed | PublishReadyToRun | WindowsAppSdkDeploymentManagerInitialize | PublishedFileCount | PublishedTotalBytes | ProcessStatus | ExitCodeHex | 読み取り |
| --- | --- | --- | --- | --- | ---: | ---: | --- | --- | --- |
| `release-baseline` | `Release` | `true` | `true` | `false` | 555 | 291,731,503 | `exited` | `0xC000027B` | 現行条件で再現 |
| `release-no-trim` | `Release` | `false` | `true` | `false` | 722 | 438,921,404 | `exited` | `0xC000027B` | trim を外しても変化なし |
| `release-no-r2r` | `Release` | `true` | `false` | `false` | 555 | 259,038,767 | `exited` | `0xC000027B` | ReadyToRun を外しても変化なし |
| `release-init-enabled` | `Release` | `true` | `true` | `true` | 556 | 291,778,186 | `exited` | `0xE0434352` | 失敗モードが変化し、別段階まで進んでいる可能性 |
| `debug-baseline` | `Debug` | `false` | `false` | `false` | 722 | 388,688,204 | `exited` | `0xC000027B` | Debug publish でも再現 |

## 5. 判断

### 5.1 今回の比較で弱くなった候補
- `PublishTrimmed=true`
- `PublishReadyToRun=true`
- `Release` 固有条件

これらを個別に外しても `0xC000027B` は継続した。

### 5.2 今回の比較で強くなった候補
- `WindowsAppSdkDeploymentManagerInitialize=false`

この値だけを `true` に変えた `release-init-enabled` では、`0xC000027B` ではなく `0xE0434352` に変わった。成功ではないが、失敗地点が変わったと見てよい。

### 5.3 現時点の最有力仮説
- unpackaged self-contained WinUI 3 / Windows App SDK 1.8 系では、`WindowsAppSdkDeploymentManagerInitialize=false` が `0xC000027B` の直接要因である可能性がある
- ただし `true` にしても別の例外で終了するため、単独での解決条件とはまだ言えない

## 6. 次に試す優先順
1. `WindowsAppSdkDeploymentManagerInitialize=true` を前提に、`0xE0434352` の例外内容を追加で取る
2. `#188` で失敗時の診断経路を整理する前に、成立条件をさらに 1 段階詰める
3. 必要なら `WindowsAppSDK version` 差分比較を追加し、`1.8.260416003` 固有かどうかを確認する

## 7. 実行コマンド

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-SelfContainedMatrix.ps1 `
  -Platform x64 `
  -RuntimeIdentifier win-x64 `
  -OutputRoot .\temp\self-contained-matrix `
  -WaitSeconds 30
```
