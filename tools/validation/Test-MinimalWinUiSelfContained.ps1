[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$WorkingDirectory,
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Join-Path $repoRoot "temp\minimal-winui-selfcontained"
}

$WorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)

function Set-ProjectValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $propertyGroup = $ProjectXml.Project.PropertyGroup | Select-Object -First 1
    $node = $propertyGroup.SelectSingleNode($Name)
    if ($null -eq $node) {
        $node = $ProjectXml.CreateElement($Name)
        $propertyGroup.AppendChild($node) | Out-Null
    }

    $node.InnerText = $Value
}

function Save-ProjectXml {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $ProjectXml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Initialize-MinimalProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectDirectory
    )

    dotnet new winui -n MinimalWinUiProbe -o $ProjectDirectory | Out-Null

    $projectPath = Join-Path $ProjectDirectory "MinimalWinUiProbe.csproj"
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Minimal WinUI project was not created: $projectPath"
    }

    return $projectPath
}

function Set-AppAlignedConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
    Set-ProjectValue $projectXml "AppxPackage" "false"
    Set-ProjectValue $projectXml "Platforms" "x64"
    Set-ProjectValue $projectXml "PlatformTarget" "x64"
    Set-ProjectValue $projectXml "RuntimeIdentifiers" "win-x64"
    Set-ProjectValue $projectXml "PublishProfile" "win-x64.pubxml"
    Set-ProjectValue $projectXml "EnableMsixTooling" "false"
    Set-ProjectValue $projectXml "WindowsPackageType" "None"
    Set-ProjectValue $projectXml "WindowsAppSdkSelfContained" "true"
    Set-ProjectValue $projectXml "WindowsAppSdkDeploymentManagerInitialize" "false"

    foreach ($packageReference in $projectXml.Project.ItemGroup.PackageReference) {
        switch ($packageReference.Include) {
            "Microsoft.Windows.SDK.BuildTools" { $packageReference.Version = "10.0.28000.1721" }
            "Microsoft.WindowsAppSDK" { $packageReference.Version = "1.8.260416003" }
        }
    }

    Save-ProjectXml $projectXml $ProjectPath
}

function Invoke-PublishProbe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$ScenarioName,
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    if (Test-Path -LiteralPath $OutputDirectory) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }

    dotnet publish $ProjectPath `
        -c $Configuration `
        -p:Platform=$Platform `
        -r $RuntimeIdentifier `
        --self-contained true `
        -o $OutputDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ScenarioName with exit code $LASTEXITCODE"
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $exePath = Join-Path $OutputDirectory "$projectName.exe"
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Published executable was not found for ${ScenarioName}: $exePath"
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File)
    $totalBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum

    $process = Start-Process -FilePath $exePath -WorkingDirectory $OutputDirectory -PassThru
    Start-Sleep -Seconds $WaitSeconds

    $exitCode = $null
    $status = "running"
    if ($process.HasExited) {
        $status = "exited"
        $exitCode = $process.ExitCode
    }
    else {
        Stop-Process -Id $process.Id -Force
    }

    [pscustomobject]@{
        Scenario = $ScenarioName
        Configuration = $Configuration
        Platform = $Platform
        RuntimeIdentifier = $RuntimeIdentifier
        ProjectPath = $ProjectPath
        OutputDirectory = $OutputDirectory
        ExecutablePath = $exePath
        WaitSeconds = $WaitSeconds
        PublishedFileCount = $publishedFiles.Count
        PublishedTotalBytes = $totalBytes
        ProcessStatus = $status
        ExitCode = $exitCode
        ExitCodeHex = if ($null -eq $exitCode) { $null } else { ('0x{0:X8}' -f ($exitCode -band 0xffffffff)) }
    }
}

if (Test-Path -LiteralPath $WorkingDirectory) {
    Remove-Item -LiteralPath $WorkingDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $WorkingDirectory | Out-Null

$defaultProjectRoot = Join-Path $WorkingDirectory "default-template"
$defaultProjectPath = Initialize-MinimalProject -ProjectDirectory $defaultProjectRoot
$defaultResult = Invoke-PublishProbe `
    -ProjectPath $defaultProjectPath `
    -ScenarioName "default-template" `
    -OutputDirectory (Join-Path $WorkingDirectory "publish-default")

$alignedProjectRoot = Join-Path $WorkingDirectory "app-aligned"
$alignedProjectPath = Initialize-MinimalProject -ProjectDirectory $alignedProjectRoot
Set-AppAlignedConfiguration -ProjectPath $alignedProjectPath
$alignedResult = Invoke-PublishProbe `
    -ProjectPath $alignedProjectPath `
    -ScenarioName "app-aligned" `
    -OutputDirectory (Join-Path $WorkingDirectory "publish-app-aligned")

@($defaultResult, $alignedResult) | Format-Table -AutoSize
