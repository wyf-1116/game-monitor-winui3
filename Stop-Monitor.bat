@echo off
setlocal

set "PORT=8765"
set "PID="

for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:":%PORT% .*LISTENING"') do (
  set "PID=%%P"
  goto stop
)

echo Game Monitor is not running on port %PORT%.
pause
exit /b 0

:stop
taskkill /PID %PID% /F >nul 2>nul
if errorlevel 1 (
  echo Failed to stop Game Monitor process %PID%.
  pause
  exit /b 1
)

echo Game Monitor stopped. PID %PID%.
timeout /t 2 >nul
