@echo off
REM DC.QQ.TG - Cross-Platform Messaging Configuration Script for Windows (Batch)
REM This script is a wrapper that calls the PowerShell configuration script

echo DC.QQ.TG - Cross-Platform Messaging Configuration Script
echo This script will help you reconfigure the DC.QQ.TG application.
echo.

REM Check if PowerShell is available
where powershell >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo Error: PowerShell is not available on this system.
    echo Please install PowerShell or use the config.ps1 script directly.
    pause
    exit /b 1
)

REM Get the directory of this batch file
set "SCRIPT_DIR=%~dp0"

REM Check if config.ps1 exists in the same directory
if exist "%SCRIPT_DIR%config.ps1" (
    echo Found configuration script at: %SCRIPT_DIR%config.ps1
    echo Running PowerShell configuration script...
    echo.

    REM Save current directory
    set "CURRENT_DIR=%CD%"

    REM Change to script directory to ensure relative paths work correctly
    cd /d "%SCRIPT_DIR%"

    REM Run the PowerShell script
    powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%config.ps1"

    REM Restore original directory
    cd /d "%CURRENT_DIR%"
) else (
    echo Error: Could not find config.ps1 in the same directory.
    echo Please make sure config.ps1 is in the same directory as this batch file.
    pause
    exit /b 1
)

echo.
echo Configuration complete. Press any key to exit...
pause >nul
