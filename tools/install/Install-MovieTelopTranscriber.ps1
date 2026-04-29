[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Version = "0.1.1",
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\MovieTelopTranscriber"),
    [string]$OcrRuntimeRoot = (Join-Path $env:LOCALAPPDATA "Programs\MovieTelopTranscriber\ocr-runtime"),
    [string]$DownloadRoot = (Join-Path $env:TEMP "movie-telop-transcriber-install"),
    [string]$PackageZipPath,
    [string]$ReleaseAssetUrl,
    [string]$PythonCommand = "py",
    [string[]]$PythonArguments = @("-3.10"),
    [switch]$SkipAppInstall,
    [switch]$SkipOcrSetup,
    [switch]$SkipModelDownload,
    [switch]$NoStartMenuShortcut,
    [switch]$Launch,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repository = "Sunmax0731/movie-telop-transcriber"
$packageName = "movie-telop-transcriber-win-x64-v$Version"
if ([string]::IsNullOrWhiteSpace($ReleaseAssetUrl)) {
    $ReleaseAssetUrl = "https://github.com/$repository/releases/download/v$Version/$packageName.zip"
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$OcrRuntimeRoot = [System.IO.Path]::GetFullPath($OcrRuntimeRoot)
$DownloadRoot = [System.IO.Path]::GetFullPath($DownloadRoot)
$downloadZipPath = Join-Path $DownloadRoot "$packageName.zip"
$expandedRoot = Join-Path $DownloadRoot "expanded"
$appDir = Join-Path $InstallRoot "app"
$appExe = Join-Path $appDir "MovieTelopTranscriber.App.exe"
$venvDir = Join-Path $OcrRuntimeRoot ".venv"
$venvPython = Join-Path $venvDir "Scripts\python.exe"
$launcherPath = Join-Path $InstallRoot "Start-MovieTelopTranscriber.ps1"
$modelRoot = Join-Path $env:USERPROFILE ".paddlex\official_models"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoWorkerPath = Join-Path $scriptRoot "..\ocr\paddle_ocr_worker.py"
$installedWorkerPath = Join-Path $appDir "tools\ocr\paddle_ocr_worker.py"

function Write-InstallLog {
    param([Parameter(Mandatory = $true)][string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] $Message"
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    Write-InstallLog ("> {0} {1}" -f $FilePath, ($Arguments -join " "))
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("Command failed with exit code {0}: {1}" -f $LASTEXITCODE, $FilePath)
    }
}

function Assert-InstallChildPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $rootFullPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') + '\'
    $targetFullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $targetFullPath.StartsWith($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside InstallRoot: $targetFullPath"
    }
}

function Remove-ExistingInstallChild {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $targetPath = Join-Path $InstallRoot $RelativePath
    Assert-InstallChildPath -Path $targetPath
    if (Test-Path -LiteralPath $targetPath) {
        Remove-Item -LiteralPath $targetPath -Recurse -Force
    }
}

function Find-PackageRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    $expectedRoot = Join-Path $Root $packageName
    if (Test-Path -LiteralPath (Join-Path $expectedRoot "app\MovieTelopTranscriber.App.exe")) {
        return $expectedRoot
    }

    if (Test-Path -LiteralPath (Join-Path $Root "app\MovieTelopTranscriber.App.exe")) {
        return $Root
    }

    $candidate = Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "app\MovieTelopTranscriber.App.exe") } |
        Select-Object -First 1
    if ($candidate) {
        return $candidate.FullName
    }

    throw "Package root was not found under: $Root"
}

function Write-Launcher {
    $launcherContent = @"
`$ErrorActionPreference = "Stop"
`$InstallRoot = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$AppDir = Join-Path `$InstallRoot "app"

`$env:MOVIE_TELOP_OCR_ENGINE = "paddleocr"
`$env:MOVIE_TELOP_PADDLEOCR_PYTHON = "$venvPython"
`$env:MOVIE_TELOP_PADDLEOCR_DEVICE = "cpu"
`$env:MOVIE_TELOP_PADDLEOCR_MIN_SCORE = "0.5"
`$env:MOVIE_TELOP_PADDLEOCR_PREPROCESS = "true"
`$env:MOVIE_TELOP_PADDLEOCR_CONTRAST = "1.1"
`$env:MOVIE_TELOP_PADDLEOCR_SHARPEN = "true"

Start-Process -FilePath (Join-Path `$AppDir "MovieTelopTranscriber.App.exe") -WorkingDirectory `$AppDir
"@

    [System.IO.File]::WriteAllText($launcherPath, $launcherContent, [System.Text.UTF8Encoding]::new($false))
}

function New-StartMenuShortcut {
    $shortcutDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Movie Telop Transcriber"
    New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null

    $shortcutPath = Join-Path $shortcutDirectory "Movie Telop Transcriber.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = "powershell.exe"
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$launcherPath`""
    $shortcut.WorkingDirectory = $InstallRoot
    $shortcut.IconLocation = "$appExe,0"
    $shortcut.Save()
}

function Resolve-WorkerPath {
    if (Test-Path -LiteralPath $installedWorkerPath -PathType Leaf) {
        return $installedWorkerPath
    }

    $repoWorkerFullPath = [System.IO.Path]::GetFullPath($repoWorkerPath)
    if (Test-Path -LiteralPath $repoWorkerFullPath -PathType Leaf) {
        return $repoWorkerFullPath
    }

    throw "paddle_ocr_worker.py was not found. Install the app first or run this installer from the repository."
}

if ($WhatIfPreference) {
    [pscustomobject]@{
        Mode = "WhatIf"
        Version = $Version
        InstallRoot = $InstallRoot
        OcrRuntimeRoot = $OcrRuntimeRoot
        ReleaseAssetUrl = $ReleaseAssetUrl
        PackageZipPath = $PackageZipPath
        PythonCommand = $PythonCommand
        PythonArguments = ($PythonArguments -join " ")
        SkipAppInstall = [bool]$SkipAppInstall
        SkipOcrSetup = [bool]$SkipOcrSetup
        SkipModelDownload = [bool]$SkipModelDownload
        LauncherPath = $launcherPath
    }
    return
}

Write-InstallLog "InstallRoot: $InstallRoot"
Write-InstallLog "OcrRuntimeRoot: $OcrRuntimeRoot"

if (-not $SkipAppInstall) {
    if ((Test-Path -LiteralPath $appDir) -and -not $Force) {
        throw "App is already installed at $appDir. Re-run with -Force to replace app/docs/samples."
    }

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $DownloadRoot -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($PackageZipPath)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-InstallLog "Downloading app package: $ReleaseAssetUrl"
        if ($PSCmdlet.ShouldProcess($downloadZipPath, "Download app package")) {
            Invoke-WebRequest -Uri $ReleaseAssetUrl -OutFile $downloadZipPath -UseBasicParsing
        }
        $PackageZipPath = $downloadZipPath
    } else {
        $PackageZipPath = [System.IO.Path]::GetFullPath($PackageZipPath)
    }

    if (-not (Test-Path -LiteralPath $PackageZipPath -PathType Leaf)) {
        throw "Package zip was not found: $PackageZipPath"
    }

    if (Test-Path -LiteralPath $expandedRoot) {
        Remove-Item -LiteralPath $expandedRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $expandedRoot -Force | Out-Null

    Write-InstallLog "Expanding app package: $PackageZipPath"
    if ($PSCmdlet.ShouldProcess($expandedRoot, "Expand app package")) {
        Expand-Archive -LiteralPath $PackageZipPath -DestinationPath $expandedRoot -Force
    }

    $packageRoot = Find-PackageRoot -Root $expandedRoot
    if ($Force) {
        Remove-ExistingInstallChild -RelativePath "app"
        Remove-ExistingInstallChild -RelativePath "docs"
        Remove-ExistingInstallChild -RelativePath "samples"
    }

    Write-InstallLog "Copying app files"
    if ($PSCmdlet.ShouldProcess($InstallRoot, "Install app files")) {
        Get-ChildItem -LiteralPath $packageRoot -Force | Copy-Item -Destination $InstallRoot -Recurse -Force
    }
}

if (-not $SkipOcrSetup) {
    New-Item -ItemType Directory -Path $OcrRuntimeRoot -Force | Out-Null

    if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        Write-InstallLog "Creating Python virtual environment"
        $venvArgs = @()
        $venvArgs += $PythonArguments
        $venvArgs += @("-m", "venv", $venvDir)
        if ($PSCmdlet.ShouldProcess($venvDir, "Create Python virtual environment")) {
            Invoke-CheckedCommand -FilePath $PythonCommand -Arguments $venvArgs
        }
    }

    if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        throw "Python virtual environment was not created: $venvPython"
    }

    if ($PSCmdlet.ShouldProcess($venvPython, "Install PaddleOCR runtime packages")) {
        Invoke-CheckedCommand -FilePath $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip")
        Invoke-CheckedCommand -FilePath $venvPython -Arguments @("-m", "pip", "install", "paddlepaddle==3.2.0", "-i", "https://www.paddlepaddle.org.cn/packages/stable/cpu/")
        Invoke-CheckedCommand -FilePath $venvPython -Arguments @("-m", "pip", "install", "paddleocr==3.5.0")
    }

    if (-not $SkipModelDownload) {
        $workerPath = Resolve-WorkerPath
        $env:MOVIE_TELOP_PADDLEOCR_DEVICE = "cpu"
        $env:MOVIE_TELOP_PADDLEOCR_PREPROCESS = "false"

        Write-InstallLog "Downloading and warming PaddleOCR models"
        if ($PSCmdlet.ShouldProcess($modelRoot, "Warm up PaddleOCR models")) {
            Invoke-CheckedCommand -FilePath $venvPython -Arguments @($workerPath, "--warmup-models", "--warmup-language", "ja")
        }

        $knownModels = @(
            "PP-OCRv5_server_det",
            "PP-OCRv5_server_rec"
        )
        $missingModels = @($knownModels | Where-Object { -not (Test-Path -LiteralPath (Join-Path $modelRoot $_)) })
        if ($missingModels.Count -gt 0) {
            Write-Warning ("PaddleOCR model folders were not found at the expected path: {0}" -f ($missingModels -join ", "))
            Write-Warning "The models may have been stored in a PaddleOCR-specific cache path. Check the warmup output above if OCR fails."
        }
    }
}

if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Application executable was not found: $appExe"
}

if (-not $SkipOcrSetup -and -not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "PaddleOCR Python was not found: $venvPython"
}

Write-InstallLog "Writing launcher: $launcherPath"
if ($PSCmdlet.ShouldProcess($launcherPath, "Write launcher")) {
    Write-Launcher
}

if (-not $NoStartMenuShortcut) {
    Write-InstallLog "Creating Start Menu shortcut"
    if ($PSCmdlet.ShouldProcess("Start Menu", "Create shortcut")) {
        New-StartMenuShortcut
    }
}

if ($Launch) {
    Write-InstallLog "Launching app"
    if ($PSCmdlet.ShouldProcess($appExe, "Launch app")) {
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcherPath
    }
}

[pscustomobject]@{
    Version = $Version
    InstallRoot = $InstallRoot
    AppExe = $appExe
    LauncherPath = $launcherPath
    OcrRuntimeRoot = $OcrRuntimeRoot
    PaddleOcrPython = $venvPython
    ModelRoot = $modelRoot
}
