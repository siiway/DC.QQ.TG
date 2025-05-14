#
# DC.QQ.TG - Cross-Platform Messaging Configuration Script for Windows (PowerShell)
# This script allows you to reconfigure the DC.QQ.TG application
#

# Function to write colored output
function Write-ColorOutput {
    param(
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

# Function to write a header
function Write-Header {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title
    )

    Write-Output ""
    Write-ColorOutput "===== $Title =====" "Cyan"
}

# Function to get configuration value with prompt
function Get-ConfigValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,

        [Parameter(Mandatory = $false)]
        [string]$Default = "",

        [Parameter(Mandatory = $false)]
        [bool]$IsSecret = $false
    )

    if ($IsSecret) {
        if ([string]::IsNullOrEmpty($Default)) {
            $value = Read-Host "$Prompt (default: keep empty if not used)"
        } else {
            $value = Read-Host "$Prompt (default: use existing value)"
            if ([string]::IsNullOrEmpty($value)) {
                $value = $Default
            }
        }
    } else {
        if ([string]::IsNullOrEmpty($Default)) {
            $value = Read-Host "$Prompt"
        } else {
            $value = Read-Host "$Prompt (default: $Default)"
            if ([string]::IsNullOrEmpty($value)) {
                $value = $Default
            }
        }
    }

    return $value
}

# Function to parse JSON configuration
function Get-JsonValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Json,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        $jsonObj = $Json | ConvertFrom-Json
        $pathParts = $Path -split '\.'
        $current = $jsonObj

        foreach ($part in $pathParts) {
            $current = $current.$part
        }

        return $current
    } catch {
        return ""
    }
}

# Display welcome message
Clear-Host
Write-ColorOutput "DC.QQ.TG - Cross-Platform Messaging Configuration Script" "Green"
Write-Output "This script will help you reconfigure the DC.QQ.TG application."
Write-ColorOutput "Press Ctrl+C at any time to cancel the configuration." "Yellow"
Write-Output ""

# Find the application directory
Write-Header "Application Directory"

# Check if the script is being run from the application directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$defaultInstallDir = Join-Path $env:USERPROFILE "DC.QQ.TG"

if (Test-Path (Join-Path $scriptDir "publish\appsettings.json")) {
    $installDir = $scriptDir
    Write-ColorOutput "Application directory detected: $installDir" "Green"
} elseif (Test-Path (Join-Path $defaultInstallDir "publish\appsettings.json")) {
    $installDir = $defaultInstallDir
    Write-ColorOutput "Application directory found at default location: $installDir" "Green"
} else {
    $installDir = Read-Host "Enter the path to the DC.QQ.TG application directory"
    if (-not (Test-Path (Join-Path $installDir "publish\appsettings.json"))) {
        Write-ColorOutput "Could not find appsettings.json in $installDir\publish" "Red"
        Write-ColorOutput "Please make sure you enter the correct directory." "Red"
        exit 1
    }
}

# Configuration setup
Write-Header "Configuration Setup"
$configPath = Join-Path $installDir "publish\appsettings.json"
Write-Output "Configuration file: $configPath"

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
$telegram_webhook_url = ""
$telegram_webhook_port = "8443"
$show_napcat_response = "false"
$enable_shell = "false"
$disable_qq = "false"
$disable_discord = "false"
$disable_telegram = "false"

# Try to load existing configuration
if (Test-Path $configPath) {
    Write-Output "Loading existing configuration..."

    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json

        # NapCat
        $napcat_url = $config.NapCat.BaseUrl
        $napcat_token = $config.NapCat.Token
        $napcat_group_id = $config.NapCat.GroupId

        # Discord
        $discord_webhook_url = $config.Discord.WebhookUrl
        $discord_bot_token = $config.Discord.BotToken
        $discord_guild_id = $config.Discord.GuildId
        $discord_channel_id = $config.Discord.ChannelId
        $discord_use_proxy = $config.Discord.UseProxy
        $discord_auto_webhook = $config.Discord.AutoWebhook
        $discord_webhook_name = $config.Discord.WebhookName

        # Telegram
        $telegram_bot_token = $config.Telegram.BotToken
        $telegram_chat_id = $config.Telegram.ChatId
        $telegram_webhook_url = $config.Telegram.WebhookUrl

        # Disabled
        $disable_qq = $config.Disabled.QQ
        $disable_discord = $config.Disabled.Discord
        $disable_telegram = $config.Disabled.Telegram

        # Debug
        $show_napcat_response = $config.Debug.ShowNapCatResponse.ToString().ToLower()
        $enable_shell = $config.Debug.EnableShell.ToString().ToLower()

        Write-ColorOutput "Existing configuration loaded." "Green"
    } catch {
        Write-ColorOutput "Error loading existing configuration: $_" "Yellow"
        Write-ColorOutput "Starting with default values." "Yellow"
    }
}

Write-ColorOutput "Let's configure your application. You can leave fields empty if you don't want to use that platform." "Cyan"
Write-ColorOutput "Note: You must configure at least one platform (QQ, Discord, or Telegram)." "Yellow"

# Platform selection
Write-Header "Platform Selection"
$use_qq = Read-Host "Do you want to use QQ? (Y/N, default: Y)"
if ([string]::IsNullOrEmpty($use_qq)) { $use_qq = "Y" }
if ($use_qq -ne "Y" -and $use_qq -ne "y") {
    $disable_qq = "true"
    Write-ColorOutput "QQ platform disabled." "Yellow"
}

$use_discord = Read-Host "Do you want to use Discord? (Y/N, default: Y)"
if ([string]::IsNullOrEmpty($use_discord)) { $use_discord = "Y" }
if ($use_discord -ne "Y" -and $use_discord -ne "y") {
    $disable_discord = "true"
    Write-ColorOutput "Discord platform disabled." "Yellow"
}

$use_telegram = Read-Host "Do you want to use Telegram? (Y/N, default: Y)"
if ([string]::IsNullOrEmpty($use_telegram)) { $use_telegram = "Y" }
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
    $napcat_token = Get-ConfigValue -Prompt "Enter NapCat API token" -Default $napcat_token -IsSecret $true
    $napcat_group_id = Get-ConfigValue -Prompt "Enter QQ group ID" -Default $napcat_group_id
}

# Discord Configuration
if ($disable_discord -ne "true") {
    Write-Header "Discord Configuration"
    Write-Output "You can use either a webhook URL or a bot token with guild and channel IDs."

    $use_webhook = Read-Host "Do you want to use a webhook? (Y/N, default: Y)"
    if ([string]::IsNullOrEmpty($use_webhook)) { $use_webhook = "Y" }

    if ($use_webhook -eq "Y" -or $use_webhook -eq "y") {
        $discord_webhook_url = Get-ConfigValue -Prompt "Enter Discord webhook URL" -Default $discord_webhook_url -IsSecret $true
        $discord_auto_webhook = "false"  # No need for auto-webhook if URL is provided
    } else {
        $discord_bot_token = Get-ConfigValue -Prompt "Enter Discord bot token" -Default $discord_bot_token -IsSecret $true
        $discord_guild_id = Get-ConfigValue -Prompt "Enter Discord guild (server) ID" -Default $discord_guild_id
        $discord_channel_id = Get-ConfigValue -Prompt "Enter Discord channel ID" -Default $discord_channel_id

        $auto_webhook = Read-Host "Do you want to enable auto-webhook creation? (Y/N, default: Y)"
        if ([string]::IsNullOrEmpty($auto_webhook)) { $auto_webhook = "Y" }
        if ($auto_webhook -eq "Y" -or $auto_webhook -eq "y") {
            $discord_auto_webhook = "true"
            $discord_webhook_name = Get-ConfigValue -Prompt "Enter webhook name" -Default $discord_webhook_name
        } else {
            $discord_auto_webhook = "false"
        }
    }

    $use_proxy = Read-Host "Do you want to use a proxy for Discord API calls? (Y/N, default: Y)"
    if ([string]::IsNullOrEmpty($use_proxy)) { $use_proxy = "Y" }
    if ($use_proxy -eq "Y" -or $use_proxy -eq "y") {
        $discord_use_proxy = "true"
    } else {
        $discord_use_proxy = "false"
    }
}

# Telegram Configuration
if ($disable_telegram -ne "true") {
    Write-Header "Telegram Configuration"
    $telegram_bot_token = Get-ConfigValue -Prompt "Enter Telegram bot token" -Default $telegram_bot_token -IsSecret $true
    $telegram_chat_id = Get-ConfigValue -Prompt "Enter Telegram chat ID" -Default $telegram_chat_id

    $use_webhook = Read-Host "Do you want to use a webhook for Telegram? (Y/N, default: N)"
    if ($use_webhook -eq "Y" -or $use_webhook -eq "y") {
        Write-ColorOutput "Note: Telegram webhooks require a publicly accessible HTTPS server." "Yellow"
        Write-ColorOutput "Important: Include the port in your webhook URL if using a non-standard port (e.g., https://your-domain.com:8443/telegram-webhook)" "Yellow"
        $telegram_webhook_url = Get-ConfigValue -Prompt "Enter Telegram webhook URL (e.g., https://your-domain.com:8443/telegram-webhook)" -Default $telegram_webhook_url -IsSecret $true
        $telegram_webhook_port = Get-ConfigValue -Prompt "Enter Telegram webhook port (must match the port in your URL)" -Default $telegram_webhook_port
    }
}

# Debug Configuration
Write-Header "Debug Configuration"
$show_responses = Read-Host "Do you want to show NapCat API responses? (Y/N, default: N)"
if ($show_responses -eq "Y" -or $show_responses -eq "y") {
    $show_napcat_response = "true"
} else {
    $show_napcat_response = "false"
}

$debug_shell = Read-Host "Do you want to enable the debug shell? (Y/N, default: N)"
if ($debug_shell -eq "Y" -or $debug_shell -eq "y") {
    $enable_shell = "true"
} else {
    $enable_shell = "false"
}

# Create the configuration file
Write-Output "Creating configuration file..."
$configJson = @"
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
    "ChatId": "$telegram_chat_id",
    "WebhookUrl": "$telegram_webhook_url",
    "WebhookPort": "$telegram_webhook_port"
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

Set-Content -Path $configPath -Value $configJson
Write-ColorOutput "Configuration file created at: $configPath" "Green"

# Display completion message
Write-Header "Configuration Complete"
Write-ColorOutput "DC.QQ.TG has been successfully configured!" "Green"
Write-ColorOutput "You can now run the application to use your new configuration." "Green"
