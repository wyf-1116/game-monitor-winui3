@echo off
setlocal
cd /d "%~dp0"

set "PYTHON_EXE="
set "PYTHON_ARGS="
set "SERVER_EXE="
set "SERVER_ARGS="

if exist "%~dp0python\python.exe" set "PYTHON_EXE=%~dp0python\python.exe"
if not defined PYTHON_EXE if exist "%~dp0.venv\Scripts\python.exe" set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"

if defined PYTHON_EXE goto run

where py >nul 2>nul
if %errorlevel%==0 (
  set "PYTHON_EXE=py"
  set "PYTHON_ARGS=-3"
  goto run
)

where python >nul 2>nul
if %errorlevel%==0 (
  set "PYTHON_EXE=python"
  goto run
)

echo.
echo Python 3 was not found.
echo.
echo Install Python 3 from https://www.python.org/downloads/ and enable "Add python.exe to PATH",
echo or place a portable Python runtime at:
echo   %~dp0python\python.exe
echo.
pause
exit /b 1

:run
"%PYTHON_EXE%" %PYTHON_ARGS% -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if errorlevel 1 (
  echo.
  echo Python 3.10 or newer is required.
  echo.
  pause
  exit /b 1
)

set "SERVER_EXE=%PYTHON_EXE%"
set "SERVER_ARGS=%PYTHON_ARGS%"

if /i "%PYTHON_EXE:~-10%"=="python.exe" (
  set "PYTHONW_EXE=%PYTHON_EXE:~0,-10%pythonw.exe"
  if exist "%PYTHONW_EXE%" set "SERVER_EXE=%PYTHONW_EXE%"
)

if /i "%PYTHON_EXE%"=="py" (
  where pyw >nul 2>nul
  if not errorlevel 1 (
    set "SERVER_EXE=pyw"
    set "SERVER_ARGS=%PYTHON_ARGS%"
  )
)

start "" "http://127.0.0.1:8765/"
start "Game Monitor" /b "%SERVER_EXE%" %SERVER_ARGS% app.py >"%~dp0monitor.log" 2>"%~dp0monitor.err.log"

if errorlevel 1 (
  echo.
  echo Game Monitor exited with an error.
  pause
)
