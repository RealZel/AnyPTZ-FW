@echo off
setlocal

if "%~1"=="" (
  echo Usage: flash_full_windows.bat COMx [BAUD]
  echo Example: flash_full_windows.bat COM5 921600
  exit /b 1
)

set PORT=%~1
set BAUD=%~2
if "%BAUD%"=="" set BAUD=921600

powershell -ExecutionPolicy Bypass -File "%~dp0flash_full_windows.ps1" -Port %PORT% -Baud %BAUD%
if errorlevel 1 (
  echo Flash failed.
  exit /b 1
)

echo Flash finished.
exit /b 0
