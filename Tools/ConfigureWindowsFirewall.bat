@echo off
setlocal EnableExtensions

set "SCRIPT=%~dp0ConfigureWindowsFirewall.ps1"

if not exist "%SCRIPT%" (
    echo ConfigureWindowsFirewall.ps1 was not found next to this file.
    pause
    exit /b 1
)

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Administrator permission is required. Requesting elevation...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    exit /b
)

if not "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -AppExe "%~1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
)

if not "%errorlevel%"=="0" (
    echo.
    echo Failed to configure Windows Firewall.
    pause
    exit /b 1
)

pause
