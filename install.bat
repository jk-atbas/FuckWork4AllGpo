@echo off
:: =============================================================
:: Work4allBlocker - NSSM Service Installation Script
:: Run as Administrator!
:: =============================================================

setlocal

set SERVICE_NAME=Fuck4Work4allGpo
set SERVICE_DISPLAY=Work4all Blocker Service
set SERVICE_DESCRIPTION=Blocks and removes work4all GPO installations automatically

:: Path to the executable (adjust if needed)
set EXE_DIR=%~dp0bin\Release\net10.0\win-x64\publish
set EXE_PATH=%EXE_DIR%\Fuck4Work4allGpo.exe

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script requires administrator privileges.
    echo         Right-click and select "Run as administrator".
    pause
    exit /b 1
)

:: Check if nssm is available
where nssm >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] nssm.exe not found in PATH.
    echo         Download from https://nssm.cc and add to PATH.
    pause
    exit /b 1
)

:: Check if executable exists
if not exist "%EXE_PATH%" (
    echo [ERROR] Executable not found at: %EXE_PATH%
    echo         Run 'dotnet publish' first, or adjust EXE_DIR.
    pause
    exit /b 1
)

echo.
echo === Installing %SERVICE_NAME% ===
echo Executable: %EXE_PATH%
echo.

:: Remove existing service if present
nssm status %SERVICE_NAME% >nul 2>&1
if %errorlevel% equ 0 (
    echo Stopping existing service...
    nssm stop %SERVICE_NAME% >nul 2>&1
    echo Removing existing service...
    nssm remove %SERVICE_NAME% confirm
)

:: Install the service
echo Installing service...
nssm install %SERVICE_NAME% "%EXE_PATH%"

:: Configure service properties
nssm set %SERVICE_NAME% DisplayName "%SERVICE_DISPLAY%"
nssm set %SERVICE_NAME% Description "%SERVICE_DESCRIPTION%"
nssm set %SERVICE_NAME% Start SERVICE_AUTO_START
nssm set %SERVICE_NAME% AppDirectory "%EXE_DIR%"

:: Configure restart behavior
nssm set %SERVICE_NAME% AppExit Default Restart
nssm set %SERVICE_NAME% AppRestartDelay 10000

:: Configure stdout/stderr logging via nssm
nssm set %SERVICE_NAME% AppStdout "%EXE_DIR%\logs\service-stdout.log"
nssm set %SERVICE_NAME% AppStderr "%EXE_DIR%\logs\service-stderr.log"
nssm set %SERVICE_NAME% AppStdoutCreationDisposition 4
nssm set %SERVICE_NAME% AppStderrCreationDisposition 4
nssm set %SERVICE_NAME% AppRotateFiles 1
nssm set %SERVICE_NAME% AppRotateSeconds 86400
nssm set %SERVICE_NAME% AppRotateBytes 10485760

:: Create logs directory
if not exist "%EXE_DIR%\logs" mkdir "%EXE_DIR%\logs"

echo.
echo === Service installed successfully ===
echo.
echo Starting service...
nssm start %SERVICE_NAME%

echo.
echo Service status:
nssm status %SERVICE_NAME%

echo.
echo Done! The service will now run at startup and block work4all installations.
echo Logs are written to: %EXE_DIR%\logs\
echo.
pause
