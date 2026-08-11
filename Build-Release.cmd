@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Copy the error above when reporting the test result.
  pause
) else (
  echo.
  echo Build complete. See the publish folder.
  pause
)
endlocal
