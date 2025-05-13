@echo off
REM DC.QQ.TG - Cross-Platform Messaging Update Script for Windows
REM This batch file launches the PowerShell update script

echo DC.QQ.TG - Cross-Platform Messaging Update Script
echo This script will update the DC.QQ.TG application to the latest version.
echo.

REM Check if PowerShell is available
where powershell >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo Error: PowerShell is not installed or not in PATH.
    echo Please install PowerShell before continuing.
    pause
    exit /b 1
)

echo Launching PowerShell update script...
echo.

REM Set execution policy for the current process and run the PowerShell script
powershell -ExecutionPolicy Bypass -Command "& .\update.ps1"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Update failed. Please check the error messages above.
    pause
    exit /b 1
)

echo.
echo Update completed successfully!
pause
