# #50 リリースノート作成 検証結果

## 1. 検証情報
- 対象 Issue: `#50`
- 検証日: 2026-04-29
- 検証者: Codex
- 対象バージョン: `v0.1.0`

## 2. 対象
- `docs/13_リリースノート.md`
- `Directory.Build.props`
- `src/MovieTelopTranscriber.App/Services/ExportPackageWriter.cs`
- `README.md`
- `docs/02_開発工程.md`
- `docs/11_配布構成と同梱物.md`

## 3. 確認内容
- リリースノートに、提供範囲、導入前提、配布物、既知制約、今後の予定を記載した。
- 初期リリース対象バージョンを `v0.1.0` とした。
- アセンブリの informational version を `0.1.0` に設定した。
- `segments.json` の `run_metadata.application_version` が informational version を参照するようにした。
- README と工程文書の現在タスクを `#51` へ更新した。

## 4. 検証コマンド
```powershell
$term = [string]([char]0x52b9) + [string]([char]0x304f)
rg -n $term README.md docs
git diff --check
dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64
Select-String -Path src\MovieTelopTranscriber.App\obj\x64\Release\net10.0-windows10.0.26100.0\MovieTelopTranscriber.App.AssemblyInfo.cs -Pattern 'AssemblyInformationalVersion|AssemblyVersion|AssemblyFileVersion'
```

## 5. 結果
- 禁止表現の混入: なし。
- diff 空白検査: 問題なし。
- Release build: 成功。
- アセンブリ情報: `AssemblyInformationalVersion("0.1.0")`、`AssemblyVersion("0.1.0.0")`、`AssemblyFileVersion("0.1.0.0")` を確認済み。
