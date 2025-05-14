#!/usr/bin/env pwsh
#
# DC.QQ.TG - Cross-Platform Messaging Installation Script for Windows
# This script automates the installation process for the DC.QQ.TG application
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
Write-ColorOutput "DC.QQ.TG - Cross-Platform Messaging Installation Script" "Green"
Write-ColorOutput "This script will install the DC.QQ.TG application on your system." "White"
Write-ColorOutput "Press Ctrl+C at any time to cancel the installation.`n" "Yellow"

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-ColorOutput "Warning: This script is not running with administrator privileges." "Yellow"
    Write-ColorOutput "Some operations may fail if they require elevated permissions." "Yellow"
    Write-ColorOutput "Consider restarting the script as an administrator if you encounter issues.`n" "Yellow"

    $continue = Read-Host "Do you want to continue anyway? (Y/N)"
    if ($continue -ne "Y" -and $continue -ne "y") {
        Write-ColorOutput "Installation cancelled." "Red"
        exit 1
    }
}

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
        Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/thank-you/sdk-9.0.100-windows-x64-installer" -OutFile $installerPath

        # Run the installer
        Write-ColorOutput "Running .NET 9 SDK installer..." "White"
        Start-Process -FilePath $installerPath -ArgumentList "/quiet" -Wait

        # Verify installation
        $newDotnetVersion = dotnet --version
        if ($newDotnetVersion -match "^9\.") {
            Write-ColorOutput ".NET 9 SDK installed successfully: $newDotnetVersion" "Green"
        } else {
            Write-ColorOutput "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0" "Red"
            exit 1
        }
    }
} catch {
    Write-ColorOutput ".NET SDK is not installed or not in PATH." "Yellow"
    Write-ColorOutput "Installing .NET 9 SDK automatically..." "Yellow"

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

# Determine installation directory
Write-Header "Installation Directory"
$defaultInstallDir = Join-Path $env:USERPROFILE "DC.QQ.TG"
$installDir = Read-Host "Enter installation directory (default: $defaultInstallDir)"
if ([string]::IsNullOrWhiteSpace($installDir)) {
    $installDir = $defaultInstallDir
}

# Create installation directory if it doesn't exist
if (-not (Test-Path $installDir)) {
    Write-ColorOutput "Creating directory: $installDir" "White"
    New-Item -ItemType Directory -Path $installDir | Out-Null
}

# Change to installation directory
Set-Location $installDir
Write-ColorOutput "Installation directory: $installDir" "Green"

# Check if repository is already cloned
$isRepo = Test-Path (Join-Path $installDir ".git")
if ($isRepo) {
    Write-Header "Updating Repository"
    Write-ColorOutput "Repository already exists. Pulling latest changes..." "White"
    git pull
} else {
    Write-Header "Cloning Repository"
    Write-ColorOutput "Cloning DC.QQ.TG repository..." "White"
    git clone https://github.com/siiway/DC.QQ.TG.git .
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

# Configuration setup
Write-Header "Configuration Setup"
$configPath = Join-Path $installDir "publish/appsettings.json"

# Function to get configuration values
function Get-ConfigValue {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Prompt,

        [Parameter(Mandatory = $false)]
        [string]$Default = "",

        [Parameter(Mandatory = $false)]
        [bool]$IsSecret = $false
    )

    if ($IsSecret) {
        $value = Read-Host "$Prompt (default: keep empty if not used)"
    } else {
        $value = Read-Host "$Prompt (default: $Default)"
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = $Default
        }
    }

    return $value
}

# Ask if user wants to configure the application now
Write-ColorOutput "Would you like to configure the application now?" "White"
$configureNow = Read-Host "This will ask for your API keys and settings (Y/N, default: Y)"
if ([string]::IsNullOrWhiteSpace($configureNow)) {
    $configureNow = "Y"
}

if ($configureNow -eq "Y" -or $configureNow -eq "y") {
    # Copy config.ps1 to the installation directory if it doesn't exist
    if (-not (Test-Path (Join-Path $installDir "config.ps1"))) {
        if (Test-Path (Join-Path $PSScriptRoot "config.ps1")) {
            Copy-Item -Path (Join-Path $PSScriptRoot "config.ps1") -Destination $installDir
            Write-ColorOutput "Configuration script copied to installation directory." "White"
        } else {
            Write-ColorOutput "Error: config.ps1 not found in current directory." "Red"
            Write-ColorOutput "Creating a default configuration file instead." "Red"
            $configureNow = "N"
        }
    }

    if ($configureNow -eq "Y" -or $configureNow -eq "y") {
        # Run the configuration script
        Write-ColorOutput "Running configuration script..." "White"
        $currentLocation = Get-Location
        Set-Location -Path $installDir

        try {
            & (Join-Path $installDir "config.ps1")

            # Check if configuration was successful
            if ($LASTEXITCODE -ne 0) {
                Write-ColorOutput "Configuration failed. Creating a default configuration file instead." "Red"
                $configureNow = "N"
            } else {
                Write-ColorOutput "Configuration completed successfully." "Green"
            }
        } catch {
            Write-ColorOutput "Error running configuration script: $_" "Red"
            Write-ColorOutput "Creating a default configuration file instead." "Red"
            $configureNow = "N"
        } finally {
            # Restore original location
            Set-Location -Path $currentLocation
        }
    }
}

if ($configureNow -ne "Y" -and $configureNow -ne "y") {
    # Create default configuration file
    Write-ColorOutput "Creating default configuration file..." "White"
    $configContent = @"
{
  "NapCat": {
    "BaseUrl": "ws://localhost:3001",
    "Token": "your_napcat_token_here",
    "GroupId": "your_qq_group_id_here"
  },
  "Discord": {
    "WebhookUrl": "your_discord_webhook_url_here",
    "BotToken": "your_discord_bot_token_here",
    "GuildId": "your_discord_guild_id_here",
    "ChannelId": "your_discord_channel_id_here",
    "UseProxy": "true",
    "AutoWebhook": "true",
    "WebhookName": "Cross-Platform Messenger"
  },
  "Telegram": {
    "BotToken": "your_telegram_bot_token_here",
    "ChatId": "your_telegram_chat_id_here",
    "WebhookUrl": "https://your-domain.com/telegram-webhook",
    "WebhookPort": "8443"
  },
  "Debug": {
    "ShowNapCatResponse": false,
    "EnableShell": false
  }
}
"@
    Set-Content -Path $configPath -Value $configContent
    Write-ColorOutput "Default configuration file created at: $configPath" "Green"
    Write-ColorOutput "Please edit this file to add your API keys and settings." "Yellow"
}

# Create a shortcut to run the application
Write-Header "Creating Shortcut"
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "DC.QQ.TG.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = Join-Path $installDir "publish\DC.QQ.TG.exe"
$Shortcut.WorkingDirectory = Join-Path $installDir "publish"
$Shortcut.Description = "DC.QQ.TG - Cross-Platform Messaging"
$Shortcut.Save()

Write-ColorOutput "Shortcut created on desktop: $shortcutPath" "Green"

# Display completion message
Write-Header "Installation Complete"
Write-ColorOutput "DC.QQ.TG has been successfully installed!" "Green"
Write-ColorOutput "Installation directory: $installDir" "White"
Write-ColorOutput "Executable location: $(Join-Path $installDir "publish\DC.QQ.TG.exe")" "White"

if ($configureNow -eq "Y" -or $configureNow -eq "y") {
    Write-ColorOutput "`nYour application has been configured and is ready to use!" "Green"
} else {
    Write-ColorOutput "`nBefore running the application, make sure to:" "Yellow"
    Write-ColorOutput "1. Edit the configuration file at: $configPath" "Yellow"
    Write-ColorOutput "2. Set up your API keys for the platforms you want to use" "Yellow"
}

Write-ColorOutput "`nYou can run the application by:" "White"
Write-ColorOutput "- Double-clicking the desktop shortcut" "White"
Write-ColorOutput "- Running the executable directly" "White"
Write-ColorOutput "- Using command line with parameters:" "White"
Write-ColorOutput "  cd $installDir\publish" "Cyan"
Write-ColorOutput "  .\DC.QQ.TG.exe" "Cyan"

Write-ColorOutput "`nThank you for installing DC.QQ.TG!" "Green"
