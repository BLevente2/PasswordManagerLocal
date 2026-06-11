@echo off
setlocal EnableExtensions

set "TCP_APP_RULE=PasswordManagerLocal Sync TCP App"
set "TCP_PORT_RULE=PasswordManagerLocal Sync TCP Port"
set "MDNS_APP_RULE=PasswordManagerLocal mDNS UDP App"
set "MDNS_PORT_RULE=PasswordManagerLocal mDNS UDP Port"
set "LEGACY_TCP_RULE=PasswordManagerLocal Sync TCP"
set "LEGACY_MDNS_RULE=PasswordManagerLocal mDNS UDP"
set "SYNC_PORT=26688"
set "MDNS_PORT=5353"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Administrator permission is required. Requesting elevation...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    exit /b
)

if not "%~1"=="" (
    set "APP_EXE=%~1"
) else (
    set "APP_EXE=%~dp0PasswordManagerLocal.Windows.exe"
)

echo Configuring Windows Firewall for PasswordManagerLocal local sync.
echo.
echo The required port rules are limited to LocalSubnet remote addresses.
echo Existing PasswordManagerLocal firewall rules will be replaced.
echo.

netsh advfirewall firewall delete rule name="%TCP_APP_RULE%" >nul 2>&1
netsh advfirewall firewall delete rule name="%TCP_PORT_RULE%" >nul 2>&1
netsh advfirewall firewall delete rule name="%MDNS_APP_RULE%" >nul 2>&1
netsh advfirewall firewall delete rule name="%MDNS_PORT_RULE%" >nul 2>&1
netsh advfirewall firewall delete rule name="%LEGACY_TCP_RULE%" >nul 2>&1
netsh advfirewall firewall delete rule name="%LEGACY_MDNS_RULE%" >nul 2>&1

netsh advfirewall firewall add rule name="%TCP_PORT_RULE%" dir=in action=allow enable=yes profile=any protocol=TCP localport=%SYNC_PORT% remoteip=localsubnet
if not "%errorlevel%"=="0" (
    echo Failed to add the TCP port firewall rule.
    pause
    exit /b 1
)

netsh advfirewall firewall add rule name="%MDNS_PORT_RULE%" dir=in action=allow enable=yes profile=any protocol=UDP localport=%MDNS_PORT% remoteip=localsubnet
if not "%errorlevel%"=="0" (
    echo Failed to add the UDP mDNS port firewall rule.
    pause
    exit /b 1
)

if exist "%APP_EXE%" (
    echo Adding optional executable-specific rules for:
    echo %APP_EXE%
    echo.

    netsh advfirewall firewall add rule name="%TCP_APP_RULE%" dir=in action=allow program="%APP_EXE%" enable=yes profile=any protocol=TCP localport=%SYNC_PORT% remoteip=localsubnet >nul 2>&1
    netsh advfirewall firewall add rule name="%MDNS_APP_RULE%" dir=in action=allow program="%APP_EXE%" enable=yes profile=any protocol=UDP localport=%MDNS_PORT% remoteip=localsubnet >nul 2>&1
) else (
    echo PasswordManagerLocal.Windows.exe was not found next to this file and no executable path was provided.
    echo The required port-based firewall rules were still added successfully.
    echo.
    echo Optional usage with executable-specific rules:
    echo   ConfigureWindowsFirewall.bat "C:\Full\Path\To\PasswordManagerLocal.Windows.exe"
    echo.
)

echo Windows Firewall rules were added successfully.
echo Sync TCP port: %SYNC_PORT%
echo mDNS UDP port: %MDNS_PORT%
pause
