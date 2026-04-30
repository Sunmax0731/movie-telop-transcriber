# issue #196 self-contained init有効時の例外内容採取

## 1. 概要
- 目的:
  `WindowsAppSdkDeploymentManagerInitialize=true` で `0xE0434352` になる条件の例外内容を採取する
- 実施日:
  2026-04-30

## 2. 実行条件
- `Configuration=Release`
- `Platform=x64`
- `RuntimeIdentifier=win-x64`
- `WindowsAppSdkDeploymentManagerInitialize=true`
- 実行スクリプト:
  `tools/validation/Test-SelfContainedInitEnabledDiagnostic.ps1`

## 3. 実測結果
- `ProcessStatus`:
  `exited`
- `ExitCodeHex`:
  `0xE0434352`
- `EventCount`:
  `3`

## 4. Event Log 採取結果

### 4.1 .NET Runtime
- `Id=1026`
- unhandled exception:
  `System.InvalidOperationException: プロセスにパッケージ ID がありません。`
- stack の先頭:
  - `ABI.Microsoft.Windows.ApplicationModel.WindowsAppRuntime.IDeploymentManagerStatics2Methods.Initialize(...)`
  - `Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK()`
  - `DeploymentManagerAutoInitializer.cs:line 30`

### 4.2 Application Error
- `Id=1000`
- `Faulting module name: KERNELBASE.dll`
- `Exception code: 0xe0434352`
- `Faulting package full name:` 空

### 4.3 Windows Error Reporting
- `Id=1001`
- `Event Name: APPCRASH`
- `Fault bucket 1414719964758279482`
- `Hashed bucket: 644ca024a6ceef2353a2184ffc12053a`
- report archive:
  `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_MovieTelopTransc_f19ccf22e8d1f34b776c31491ab8f58ac1566a2c_ed4197b5_4bfb143c-08fd-46b2-acae-ec2db713f592`

## 5. 判断
- `WindowsAppSdkDeploymentManagerInitialize=true` は `0xC000027B` を回避しない
- 代わりに、unpackaged self-contained 実行で `DeploymentManager.AutoInitialize` が package identity を前提として `System.InvalidOperationException` を投げている
- したがって、このアプリでは少なくとも現状の unpackaged 実行方針において `WindowsAppSdkDeploymentManagerInitialize=true` を採用候補にしない
- `#195` で見えた「失敗モードが変わる」は、成功に近づいたのではなく、別の不成立条件にぶつかった結果と判断する

## 6. 次の示唆
- self-contained 再採用を続けるなら、`WindowsAppSdkDeploymentManagerInitialize` 以外の成立条件を探す必要がある
- 一方で利用者向けには、self-contained 未成立を前提にした診断導線整理を優先してよい
- したがって次の実務優先は `#188` とする

## 7. 実行コマンド

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-SelfContainedInitEnabledDiagnostic.ps1 `
  -Configuration Release `
  -Platform x64 `
  -RuntimeIdentifier win-x64 `
  -OutputDirectory .\temp\self-contained-init-diagnostic `
  -WaitSeconds 30
```
