# issue #187 WinUI / Windows App SDK 最小構成 self-contained 比較

## 1. 概要
- 目的:
  `movie-telop-transcriber` 本体固有の問題か、WinUI / Windows App SDK の self-contained 条件で再現する問題かを切り分ける
- 比較日:
  2026-04-30
- 結論:
  `WindowsAppSDK 1.8.260416003` を使い、`WindowsPackageType=None`、`WindowsAppSdkSelfContained=true`、`WindowsAppSdkDeploymentManagerInitialize=false`、`PublishTrimmed=true` を含む本体同等条件へ寄せた最小 WinUI 3 アプリでも、self-contained publish 後に `0xC000027B` で即終了した  
  そのため、少なくとも現段階では「本体固有コードだけが原因」とは見なさず、まず `Windows App SDK 1.8 系 self-contained 条件` を優先して疑う

## 2. 比較条件

### 2.1 比較対象
1. `movie-telop-transcriber` 本体
2. `dotnet new winui` で生成した最小 WinUI アプリ
   - `default-template`
   - `app-aligned`

### 2.2 `app-aligned` で本体へ合わせた項目
- `Microsoft.WindowsAppSDK = 1.8.260416003`
- `Microsoft.Windows.SDK.BuildTools = 10.0.28000.1721`
- `Platforms = x64`
- `PlatformTarget = x64`
- `RuntimeIdentifiers = win-x64`
- `AppxPackage = false`
- `EnableMsixTooling = false`
- `WindowsPackageType = None`
- `WindowsAppSdkSelfContained = true`
- `WindowsAppSdkDeploymentManagerInitialize = false`
- publish 条件:
  `dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true`

### 2.3 実行スクリプト
- 本体 canonical:
  `tools/validation/Test-SelfContainedPublish.ps1`
- 最小 WinUI 比較:
  `tools/validation/Test-MinimalWinUiSelfContained.ps1`

## 3. 実測結果

| 対象 | 条件 | PublishedFileCount | PublishedTotalBytes | 起動結果 | ExitCodeHex |
| --- | --- | ---: | ---: | --- | --- |
| 最小 WinUI `default-template` | template 既定 (`WindowsAppSDK 2.0.1`) | 49 | 69,651,013 | 30 秒以内に終了 | `0xE0434352` |
| 最小 WinUI `app-aligned` | 本体同等 (`WindowsAppSDK 1.8.260416003`) | 322 | 132,756,669 | 30 秒以内に終了 | `0xC000027B` |
| 本体 self-contained | 本体そのまま | 555 | 291,731,503 | 30 秒以内に終了 | `0xC000027B` |

## 4. 読み取り

### 4.1 ここで除外できること
- `OpenCvSharp4.Windows`
- `CommunityToolkit.Mvvm`
- OCR worker script の同梱
- 本体の画面構成や `MainPageViewModel`

これらは最小 WinUI `app-aligned` には含まれていないが、それでも `0xC000027B` が再現した。

### 4.2 ここで疑いが強くなったもの
- `WindowsAppSDK 1.8.260416003` を含む self-contained 条件
- unpackaged WinUI 3 (`WindowsPackageType=None`) と self-contained の組み合わせ
- trim / ReadyToRun を含む publish 条件

### 4.3 まだ切り分け不足の点
- `WindowsAppSDK 1.8.260416003` 単体が要因か
- `PublishTrimmed=true` が要因か
- `PublishReadyToRun=true` が要因か
- `WindowsAppSdkDeploymentManagerInitialize=false` が要因か

## 5. 原因候補の絞り込み結果
- 元の仮説:
  本体アプリ固有の依存またはコードが self-contained 起動失敗の原因かもしれない
- 今回の結果:
  本体同等条件へ寄せた最小 WinUI 3 アプリでも同じ `0xC000027B` を再現した
- 更新後の判断:
  原因候補は `アプリ固有実装` より `Windows App SDK 1.8 系 self-contained publish 条件` 側に寄った

## 6. 次の比較候補
- `#195`:
  `PublishTrimmed` / `PublishReadyToRun` / `WindowsAppSdkDeploymentManagerInitialize` / `WindowsAppSDK version` の組み合わせ比較
- `#188`:
  失敗時の診断情報を利用者向けにどう見せるかの整理

## 7. 実行コマンド

### 7.1 最小 WinUI 比較
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-MinimalWinUiSelfContained.ps1 `
  -Configuration Release `
  -Platform x64 `
  -RuntimeIdentifier win-x64 `
  -WorkingDirectory .\temp\minimal-winui-selfcontained `
  -WaitSeconds 30
```

### 7.2 本体 canonical 再確認
```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\validation\Test-SelfContainedPublish.ps1 `
  -Configuration Release `
  -Platform x64 `
  -RuntimeIdentifier win-x64 `
  -OutputDirectory .\temp\self-contained-repro-issue187 `
  -WaitSeconds 30
```
