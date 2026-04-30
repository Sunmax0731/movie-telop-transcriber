# release担当向け短縮ガイド

## 1. release 前に最初に確認するもの
1. [17_現在状態サマリ.md](17_現在状態サマリ.md)
2. [13_リリースノート.md](13_リリースノート.md)
3. [18_配布物manifest.md](18_配布物manifest.md)
4. [test-results/00_検証成果物ガイド.md](test-results/00_検証成果物ガイド.md)

## 2. 最低限の実行項目
1. `dotnet build src\MovieTelopTranscriber.sln -c Release -p:Platform=x64 --no-restore`
2. `dotnet test src\MovieTelopTranscriber.App.Tests\MovieTelopTranscriber.App.Tests.csproj -c Release -m:1`
3. `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\validation\Test-ReleaseSmoke.ps1 -Version <version>`
4. 生成された release レポートと checksum を確認する
5. GitHub Release を公開し、release issue に URL と checksum を残して close する

## 3. 参照先
- release 仕様:
  [13_リリースノート.md](13_リリースノート.md)
- 配布物一覧:
  [18_配布物manifest.md](18_配布物manifest.md)
- 検証成果物:
  [test-results/00_検証成果物ガイド.md](test-results/00_検証成果物ガイド.md)
- 現在の issue / 次アクション:
  [17_現在状態サマリ.md](17_現在状態サマリ.md)
