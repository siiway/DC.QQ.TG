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

# Initialize configuration variables with defaults
$napcat_url = "ws://localhost:3001"
$napcat_token = ""
$napcat_group_id = ""
$discord_webhook_url = ""
$discord_bot_token = ""
$discord_guild_id = ""
$discord_channel_id = ""
$discord_use_proxy = "true"
$discord_auto_webhook = "true"
$discord_webhook_name = "Cross-Platform Messenger"
$telegram_bot_token = ""
$telegram_chat_id = ""
$show_napcat_response = "false"
$enable_shell = "false"
$disable_qq = "false"
$disable_discord = "false"
$disable_telegram = "false"

if ($configureNow -eq "Y" -or $configureNow -eq "y") {
    Write-ColorOutput "Let's configure your application. You can leave fields empty if you don't want to use that platform." "Cyan"
    Write-ColorOutput "Note: You must configure at least one platform (QQ, Discord, or Telegram)." "Yellow"

    # Platform selection
    Write-Header "Platform Selection"
    $use_qq = Read-Host "Do you want to use QQ? (Y/N, default: Y)"
    if ([string]::IsNullOrWhiteSpace($use_qq)) {
        $use_qq = "Y"
    }
    if ($use_qq -ne "Y" -and $use_qq -ne "y") {
        $disable_qq = "true"
        Write-ColorOutput "QQ platform disabled." "Yellow"
    }

    $use_discord = Read-Host "Do you want to use Discord? (Y/N, default: Y)"
    if ([string]::IsNullOrWhiteSpace($use_discord)) {
        $use_discord = "Y"
    }
    if ($use_discord -ne "Y" -and $use_discord -ne "y") {
        $disable_discord = "true"
        Write-ColorOutput "Discord platform disabled." "Yellow"
    }

    $use_telegram = Read-Host "Do you want to use Telegram? (Y/N, default: Y)"
    if ([string]::IsNullOrWhiteSpace($use_telegram)) {
        $use_telegram = "Y"
    }
    if ($use_telegram -ne "Y" -and $use_telegram -ne "y") {
        $disable_telegram = "true"
        Write-ColorOutput "Telegram platform disabled." "Yellow"
    }

    # Check if at least one platform is enabled
    if ($disable_qq -eq "true" -and $disable_discord -eq "true" -and $disable_telegram -eq "true") {
        Write-ColorOutput "Error: You must enable at least one platform." "Red"
        exit 1
    }

    # NapCat (QQ) Configuration
    if ($disable_qq -ne "true") {
        Write-Header "NapCat (QQ) Configuration"
        $napcat_url = Get-ConfigValue -Prompt "Enter NapCat WebSocket URL" -Default $napcat_url
        $napcat_token = Get-ConfigValue -Prompt "Enter NapCat API token" -IsSecret $true
        $napcat_group_id = Get-ConfigValue -Prompt "Enter QQ group ID"
    }

    # Discord Configuration
    if ($disable_discord -ne "true") {
        Write-Header "Discord Configuration"
        Write-ColorOutput "You can use either a webhook URL or a bot token with guild and channel IDs." "White"

        $use_webhook = Read-Host "Do you want to use a webhook? (Y/N, default: Y)"
        if ([string]::IsNullOrWhiteSpace($use_webhook)) {
            $use_webhook = "Y"
        }

        if ($use_webhook -eq "Y" -or $use_webhook -eq "y") {
            $discord_webhook_url = Get-ConfigValue -Prompt "Enter Discord webhook URL" -IsSecret $true
            $discord_auto_webhook = "false"  # No need for auto-webhook if URL is provided
        } else {
            $discord_bot_token = Get-ConfigValue -Prompt "Enter Discord bot token" -IsSecret $true
            $discord_guild_id = Get-ConfigValue -Prompt "Enter Discord guild (server) ID"
            $discord_channel_id = Get-ConfigValue -Prompt "Enter Discord channel ID"

            $auto_webhook = Read-Host "Do you want to enable auto-webhook creation? (Y/N, default: Y)"
            if ([string]::IsNullOrWhiteSpace($auto_webhook)) {
                $auto_webhook = "Y"
            }
            if ($auto_webhook -eq "Y" -or $auto_webhook -eq "y") {
                $discord_auto_webhook = "true"
                $discord_webhook_name = Get-ConfigValue -Prompt "Enter webhook name" -Default $discord_webhook_name
            } else {
                $discord_auto_webhook = "false"
            }
        }

        $use_proxy = Read-Host "Do you want to use a proxy for Discord API calls? (Y/N, default: Y)"
        if ([string]::IsNullOrWhiteSpace($use_proxy)) {
            $use_proxy = "Y"
        }
        if ($use_proxy -eq "Y" -or $use_proxy -eq "y") {
            $discord_use_proxy = "true"
        } else {
            $discord_use_proxy = "false"
        }
    }

    # Telegram Configuration
    if ($disable_telegram -ne "true") {
        Write-Header "Telegram Configuration"
        $telegram_bot_token = Get-ConfigValue -Prompt "Enter Telegram bot token" -IsSecret $true
        $telegram_chat_id = Get-ConfigValue -Prompt "Enter Telegram chat ID"
    }

    # Debug Configuration
    Write-Header "Debug Configuration"
    $show_responses = Read-Host "Do you want to show NapCat API responses? (Y/N, default: N)"
    if ($show_responses -eq "Y" -or $show_responses -eq "y") {
        $show_napcat_response = "true"
    }

    $debug_shell = Read-Host "Do you want to enable the debug shell? (Y/N, default: N)"
    if ($debug_shell -eq "Y" -or $debug_shell -eq "y") {
        $enable_shell = "true"
    }

    # Create the configuration file
    Write-ColorOutput "Creating configuration file..." "White"
    $configContent = @"
{
  "NapCat": {
    "BaseUrl": "$napcat_url",
    "Token": "$napcat_token",
    "GroupId": "$napcat_group_id"
  },
  "Discord": {
    "WebhookUrl": "$discord_webhook_url",
    "BotToken": "$discord_bot_token",
    "GuildId": "$discord_guild_id",
    "ChannelId": "$discord_channel_id",
    "UseProxy": "$discord_use_proxy",
    "AutoWebhook": "$discord_auto_webhook",
    "WebhookName": "$discord_webhook_name"
  },
  "Telegram": {
    "BotToken": "$telegram_bot_token",
    "ChatId": "$telegram_chat_id"
  },
  "Disabled": {
    "QQ": "$disable_qq",
    "Discord": "$disable_discord",
    "Telegram": "$disable_telegram"
  },
  "Debug": {
    "ShowNapCatResponse": $show_napcat_response,
    "EnableShell": $enable_shell
  }
}
"@
    Set-Content -Path $configPath -Value $configContent
    Write-ColorOutput "Configuration file created at: $configPath" "Green"
} else {
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
    "ChatId": "your_telegram_chat_id_here"
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
