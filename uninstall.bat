@echo off
:: =============================================================
:: Work4allBlocker - NSSM Service Uninstallation Script
:: Run as Administrator!
:: =============================================================

set SERVICE_NAME=Fuck4Work4allGpo

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script requires administrator privileges.
    pause
    exit /b 1
)

where nssm >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] nssm.exe not found in PATH.
    pause
    exit /b 1
)

echo Stopping service...
nssm stop %SERVICE_NAME% >nul 2>&1

echo Removing service...
nssm remove %SERVICE_NAME% confirm

echo.
echo Service removed.
pause
