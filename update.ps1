#!/usr/bin/env pwsh
#
# DC.QQ.TG - Cross-Platform Messaging Update Script for Windows
# This script automates the update process for the DC.QQ.TG application
#

# Set error action preference to stop on any error
$ErrorActionPreference = "Stop"

# Define colors for console output
function Write-ColorOutput {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [string]$ForegroundColor = "White"
    )
    
    $originalColor = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    Write-Output $Message
    $host.UI.RawUI.ForegroundColor = $originalColor
}

function Write-Header {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Title
    )
    
    Write-ColorOutput "`n===== $Title =====" "Cyan"
}

# Display welcome message
Clear-Host
Write-ColorOutput "DC.QQ.TG - Cross-Platform Messaging Update Script" "Green"
Write-ColorOutput "This script will update the DC.QQ.TG application to the latest version." "White"
Write-ColorOutput "Press Ctrl+C at any time to cancel the update.`n" "Yellow"

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-ColorOutput "Warning: This script is not running with administrator privileges." "Yellow"
    Write-ColorOutput "Some operations may fail if they require elevated permissions." "Yellow"
    Write-ColorOutput "Consider restarting the script as an administrator if you encounter issues.`n" "Yellow"
    
    $continue = Read-Host "Do you want to continue anyway? (Y/N)"
    if ($continue -ne "Y" -and $continue -ne "y") {
        Write-ColorOutput "Update cancelled." "Red"
        exit 1
    }
}

# Determine installation directory
Write-Header "Installation Directory"
$defaultInstallDir = Join-Path $env:USERPROFILE "DC.QQ.TG"
$installDir = Read-Host "Enter installation directory (default: $defaultInstallDir)"
if ([string]::IsNullOrWhiteSpace($installDir)) {
    $installDir = $defaultInstallDir
}

# Check if the directory exists
if (-not (Test-Path $installDir)) {
    Write-ColorOutput "Error: Directory $installDir does not exist." "Red"
    Write-ColorOutput "Please run the installation script first." "Red"
    exit 1
}

# Check if it's a git repository
if (-not (Test-Path (Join-Path $installDir ".git"))) {
    Write-ColorOutput "Error: $installDir is not a git repository." "Red"
    Write-ColorOutput "Please run the installation script first." "Red"
    exit 1
}

# Change to installation directory
Set-Location $installDir
Write-ColorOutput "Installation directory: $installDir" "Green"

# Backup configuration file
Write-Header "Backing Up Configuration"
$configPath = Join-Path $installDir "publish/appsettings.json"
$backupPath = Join-Path $installDir "publish/appsettings.backup.json"

if (Test-Path $configPath) {
    Write-ColorOutput "Backing up configuration file..." "White"
    Copy-Item -Path $configPath -Destination $backupPath -Force
    Write-ColorOutput "Configuration backed up to: $backupPath" "Green"
} else {
    Write-ColorOutput "Warning: Configuration file not found at $configPath" "Yellow"
    Write-ColorOutput "A new configuration file will be created after the update." "Yellow"
}

# Update the repository
Write-Header "Updating Repository"
Write-ColorOutput "Pulling latest changes from the repository..." "White"
git fetch
$currentBranch = git rev-parse --abbrev-ref HEAD
git pull origin $currentBranch

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "Error: Failed to pull latest changes." "Red"
    Write-ColorOutput "Please resolve any conflicts and try again." "Red"
    exit 1
}

Write-ColorOutput "Repository updated successfully." "Green"

# Check if .NET 9 SDK is installed
Write-Header "Checking .NET SDK"
try {
    $dotnetVersion = dotnet --version
    if ($dotnetVersion -match "^9\.") {
        Write-ColorOutput ".NET 9 SDK is already installed: $dotnetVersion" "Green"
    } else {
        Write-ColorOutput ".NET 9 SDK is not installed. Current version: $dotnetVersion" "Yellow"
        Write-ColorOutput "Installing .NET 9 SDK..." "Yellow"
        
        # Download .NET 9 SDK installer
        $tempDir = [System.IO.Path]::GetTempPath()
        $installerPath = Join-Path $tempDir "dotnet-sdk-9.0-win-x64.exe"
        
        Write-ColorOutput "Downloading .NET 9 SDK installer..." "White"
        try {
            # Direct download link for .NET 9 SDK
            $downloadUrl = "https://download.visualstudio.microsoft.com/download/pr/b5d46ddb-b40d-4bcd-b7d3-a5e2c9039cf1/1c1e8a7b5f9b50f3c0a8cf7c6d211a96/dotnet-sdk-9.0.100-win-x64.exe"
            Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath
            
            # Run the installer
            Write-ColorOutput "Running .NET 9 SDK installer..." "White"
            Start-Process -FilePath $installerPath -ArgumentList "/quiet" -Wait
            
            # Verify installation
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
            $dotnetVersion = dotnet --version
            if ($dotnetVersion -match "^9\.") {
                Write-ColorOutput ".NET 9 SDK installed successfully: $dotnetVersion" "Green"
            } else {
                Write-ColorOutput "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0" "Red"
                exit 1
            }
        } catch {
            Write-ColorOutput "Error downloading or installing .NET 9 SDK: $_" "Red"
            Write-ColorOutput "Please install .NET 9 SDK manually from https://dotnet.microsoft.com/download/dotnet/9.0" "Red"
            exit 1
        }
    }
} catch {
    Write-ColorOutput ".NET SDK is not installed or not in PATH." "Red"
    Write-ColorOutput "Please install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0" "Red"
    exit 1
}

# Build the application
Write-Header "Building Application"
Write-ColorOutput "Restoring dependencies..." "White"
dotnet restore

Write-ColorOutput "Building application..." "White"
dotnet publish -c Release -r win-x64 -o ./publish --self-contained

if (Test-Path (Join-Path $installDir "publish/DC.QQ.TG.exe")) {
    Write-ColorOutput "Application built successfully!" "Green"
} else {
    Write-ColorOutput "Failed to build the application." "Red"
    exit 1
}

# Restore configuration file
Write-Header "Restoring Configuration"
if (Test-Path $backupPath) {
    Write-ColorOutput "Restoring configuration file..." "White"
    Copy-Item -Path $backupPath -Destination $configPath -Force
    Write-ColorOutput "Configuration restored from: $backupPath" "Green"
} else {
    Write-ColorOutput "Warning: No backup configuration file found at $backupPath" "Yellow"
    Write-ColorOutput "You may need to reconfigure the application." "Yellow"
}

# Update the desktop shortcut
Write-Header "Updating Shortcut"
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "DC.QQ.TG.lnk"

if (Test-Path $shortcutPath) {
    Write-ColorOutput "Updating desktop shortcut..." "White"
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($shortcutPath)
    $Shortcut.TargetPath = Join-Path $installDir "publish\DC.QQ.TG.exe"
    $Shortcut.WorkingDirectory = Join-Path $installDir "publish"
    $Shortcut.Description = "DC.QQ.TG - Cross-Platform Messaging"
    $Shortcut.Save()
    Write-ColorOutput "Desktop shortcut updated: $shortcutPath" "Green"
} else {
    Write-ColorOutput "Creating desktop shortcut..." "White"
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($shortcutPath)
    $Shortcut.TargetPath = Join-Path $installDir "publish\DC.QQ.TG.exe"
    $Shortcut.WorkingDirectory = Join-Path $installDir "publish"
    $Shortcut.Description = "DC.QQ.TG - Cross-Platform Messaging"
    $Shortcut.Save()
    Write-ColorOutput "Desktop shortcut created: $shortcutPath" "Green"
}

# Display completion message
Write-Header "Update Complete"
Write-ColorOutput "DC.QQ.TG has been successfully updated!" "Green"
Write-ColorOutput "Installation directory: $installDir" "White"
Write-ColorOutput "Executable location: $(Join-Path $installDir "publish\DC.QQ.TG.exe")" "White"

Write-ColorOutput "`nYou can run the application by:" "White"
Write-ColorOutput "- Double-clicking the desktop shortcut" "White"
Write-ColorOutput "- Running the executable directly" "White"
Write-ColorOutput "- Using command line with parameters:" "White"
Write-ColorOutput "  cd $installDir\publish" "Cyan"
Write-ColorOutput "  .\DC.QQ.TG.exe" "Cyan"

Write-ColorOutput "`nThank you for updating DC.QQ.TG!" "Green"
