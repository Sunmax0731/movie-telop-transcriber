@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Uninstall-MovieTelopTranscriber.ps1"

if not exist "%PS_SCRIPT%" (
  echo Uninstall-MovieTelopTranscriber.ps1 was not found.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
  echo Uninstall failed. Exit code: %EXITCODE%
  pause
)
exit /b %EXITCODE%

