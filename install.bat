@echo off
REM DC.QQ.TG - Cross-Platform Messaging Installation Script for Windows
REM This batch file launches the PowerShell installation script

echo DC.QQ.TG - Cross-Platform Messaging Installation Script
echo This script will install the DC.QQ.TG application on your system.
echo.

REM Check if PowerShell is available
where powershell >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo Error: PowerShell is not installed or not in PATH.
    echo Please install PowerShell before continuing.
    pause
    exit /b 1
)

echo Launching PowerShell installation script...
echo.

REM Set execution policy for the current process and run the PowerShell script
powershell -ExecutionPolicy Bypass -Command "& .\install.ps1"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Installation failed. Please check the error messages above.
    pause
    exit /b 1
)

echo.
echo Installation completed successfully!
pause
