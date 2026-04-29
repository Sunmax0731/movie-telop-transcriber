@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PACKAGE_SCRIPT=%SCRIPT_DIR%Install-MovieTelopTranscriber.ps1"
set "REPO_SCRIPT=%SCRIPT_DIR%tools\install\Install-MovieTelopTranscriber.ps1"
set "PS_SCRIPT="

if exist "%PACKAGE_SCRIPT%" set "PS_SCRIPT=%PACKAGE_SCRIPT%"
if not defined PS_SCRIPT if exist "%REPO_SCRIPT%" set "PS_SCRIPT=%REPO_SCRIPT%"

if not defined PS_SCRIPT (
  echo Install-MovieTelopTranscriber.ps1 was not found.
  echo Place this command file next to the packaged installer or keep it under tools\install in the repository.
  pause
  exit /b 1
)

set "INTERACTIVE="
if "%~1"=="" set "INTERACTIVE=1"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo Installation failed. Exit code: %EXITCODE%
  pause
  exit /b %EXITCODE%
)

if defined INTERACTIVE (
  echo Installation completed.
  echo Use "Movie Telop Transcriber.cmd" in the install folder or the Start menu shortcut to launch the app.
  pause
)

exit /b 0
