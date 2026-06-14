@echo off
setlocal
cd /d "%~dp0"

set "APP=%~dp0bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\GameMonitor.exe"

if not exist "%APP%" (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo .NET 8 SDK was not found.
    echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
  )

  dotnet publish GameMonitor.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
  if errorlevel 1 (
    echo.
    echo Game Monitor could not be built.
    pause
    exit /b 1
  )
)

start "" "%APP%"
