#!/bin/bash
#
# DC.QQ.TG - Cross-Platform Messaging Configuration Script for Linux/macOS
# This script allows you to reconfigure the DC.QQ.TG application
#

# Exit on error
set -e

# Define colors for console output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to print colored output
print_color() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Function to print header
print_header() {
    local title=$1
    echo ""
    print_color "$CYAN" "===== $title ====="
}

# Function to check if a command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Function to detect OS
detect_os() {
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        echo "linux"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        echo "macos"
    else
        echo "unknown"
    fi
}

# Display welcome message
clear
print_color "$GREEN" "DC.QQ.TG - Cross-Platform Messaging Configuration Script"
print_color "$NC" "This script will help you reconfigure the DC.QQ.TG application."
print_color "$YELLOW" "Press Ctrl+C at any time to cancel the configuration."
echo ""

# Detect OS
OS=$(detect_os)
if [[ "$OS" == "unknown" ]]; then
    print_color "$RED" "Unsupported operating system. This script supports Linux and macOS only."
    exit 1
fi

print_header "System Information"
print_color "$NC" "Operating System: $OS"

# Find the application directory
print_header "Application Directory"
default_install_dir="$HOME/DC.QQ.TG"

# Check if the script is being run from the application directory
if [ -f "./publish/appsettings.json" ]; then
    install_dir="$(pwd)"
    print_color "$GREEN" "Application directory detected: $install_dir"
elif [ -f "$default_install_dir/publish/appsettings.json" ]; then
    install_dir="$default_install_dir"
    print_color "$GREEN" "Application directory found at default location: $install_dir"
else
    read -p "Enter the path to the DC.QQ.TG application directory: " install_dir
    if [ ! -f "$install_dir/publish/appsettings.json" ]; then
        print_color "$RED" "Could not find appsettings.json in $install_dir/publish"
        print_color "$RED" "Please make sure you enter the correct directory."
        exit 1
    fi
fi

# Configuration setup
print_header "Configuration Setup"
config_path="$install_dir/publish/appsettings.json"
print_color "$NC" "Configuration file: $config_path"

# Function to ask for configuration values
ask_config() {
    local prompt=$1
    local default=$2
    local is_secret=$3

    if [[ "$is_secret" == "true" ]]; then
        read -p "$prompt (default: keep empty if not used): " value
    else
        read -p "$prompt (default: $default): " value
        value=${value:-$default}
    fi

    echo "$value"
}

# Initialize configuration variables with defaults
napcat_url="ws://localhost:3001"
napcat_token=""
napcat_group_id=""
discord_webhook_url=""
discord_bot_token=""
discord_guild_id=""
discord_channel_id=""
discord_use_proxy="true"
discord_auto_webhook="true"
discord_webhook_name="Cross-Platform Messenger"
telegram_bot_token=""
telegram_chat_id=""
telegram_webhook_url=""
show_napcat_response="false"
enable_shell="false"
disable_qq="false"
disable_discord="false"
disable_telegram="false"

# Try to load existing configuration
if [ -f "$config_path" ]; then
    print_color "$NC" "Loading existing configuration..."
    
    # Extract values using grep and sed (basic parsing)
    napcat_url=$(grep -o '"BaseUrl": *"[^"]*"' "$config_path" | sed 's/"BaseUrl": *"\(.*\)"/\1/')
    napcat_token=$(grep -o '"Token": *"[^"]*"' "$config_path" | grep -v "BotToken" | sed 's/"Token": *"\(.*\)"/\1/')
    napcat_group_id=$(grep -o '"GroupId": *"[^"]*"' "$config_path" | sed 's/"GroupId": *"\(.*\)"/\1/')
    
    discord_webhook_url=$(grep -o '"WebhookUrl": *"[^"]*"' "$config_path" | sed 's/"WebhookUrl": *"\(.*\)"/\1/')
    discord_bot_token=$(grep -o '"BotToken": *"[^"]*"' "$config_path" | grep -v "Telegram" | sed 's/"BotToken": *"\(.*\)"/\1/')
    discord_guild_id=$(grep -o '"GuildId": *"[^"]*"' "$config_path" | sed 's/"GuildId": *"\(.*\)"/\1/')
    discord_channel_id=$(grep -o '"ChannelId": *"[^"]*"' "$config_path" | sed 's/"ChannelId": *"\(.*\)"/\1/')
    discord_use_proxy=$(grep -o '"UseProxy": *"[^"]*"' "$config_path" | sed 's/"UseProxy": *"\(.*\)"/\1/')
    discord_auto_webhook=$(grep -o '"AutoWebhook": *"[^"]*"' "$config_path" | sed 's/"AutoWebhook": *"\(.*\)"/\1/')
    discord_webhook_name=$(grep -o '"WebhookName": *"[^"]*"' "$config_path" | sed 's/"WebhookName": *"\(.*\)"/\1/')
    
    telegram_bot_token=$(grep -o '"BotToken": *"[^"]*"' "$config_path" | grep "Telegram" -A 1 | grep -o '"BotToken": *"[^"]*"' | sed 's/"BotToken": *"\(.*\)"/\1/')
    telegram_chat_id=$(grep -o '"ChatId": *"[^"]*"' "$config_path" | sed 's/"ChatId": *"\(.*\)"/\1/')
    telegram_webhook_url=$(grep -o '"WebhookUrl": *"[^"]*"' "$config_path" | grep "Telegram" -A 3 | grep -o '"WebhookUrl": *"[^"]*"' | sed 's/"WebhookUrl": *"\(.*\)"/\1/')
    
    disable_qq=$(grep -o '"QQ": *"[^"]*"' "$config_path" | sed 's/"QQ": *"\(.*\)"/\1/')
    disable_discord=$(grep -o '"Discord": *"[^"]*"' "$config_path" | grep "Disabled" -A 3 | grep -o '"Discord": *"[^"]*"' | sed 's/"Discord": *"\(.*\)"/\1/')
    disable_telegram=$(grep -o '"Telegram": *"[^"]*"' "$config_path" | grep "Disabled" -A 3 | grep -o '"Telegram": *"[^"]*"' | sed 's/"Telegram": *"\(.*\)"/\1/')
    
    show_napcat_response=$(grep -o '"ShowNapCatResponse": *[^,}]*' "$config_path" | sed 's/"ShowNapCatResponse": *\(.*\)/\1/')
    enable_shell=$(grep -o '"EnableShell": *[^,}]*' "$config_path" | sed 's/"EnableShell": *\(.*\)/\1/')
    
    print_color "$GREEN" "Existing configuration loaded."
fi

print_color "$CYAN" "Let's configure your application. You can leave fields empty if you don't want to use that platform."
print_color "$YELLOW" "Note: You must configure at least one platform (QQ, Discord, or Telegram)."

# Platform selection
print_header "Platform Selection"
read -p "Do you want to use QQ? (Y/N, default: Y): " use_qq
use_qq=${use_qq:-Y}
if [[ "$use_qq" != "Y" && "$use_qq" != "y" ]]; then
    disable_qq="true"
    print_color "$YELLOW" "QQ platform disabled."
fi

read -p "Do you want to use Discord? (Y/N, default: Y): " use_discord
use_discord=${use_discord:-Y}
if [[ "$use_discord" != "Y" && "$use_discord" != "y" ]]; then
    disable_discord="true"
    print_color "$YELLOW" "Discord platform disabled."
fi

read -p "Do you want to use Telegram? (Y/N, default: Y): " use_telegram
use_telegram=${use_telegram:-Y}
if [[ "$use_telegram" != "Y" && "$use_telegram" != "y" ]]; then
    disable_telegram="true"
    print_color "$YELLOW" "Telegram platform disabled."
fi

# Check if at least one platform is enabled
if [[ "$disable_qq" == "true" && "$disable_discord" == "true" && "$disable_telegram" == "true" ]]; then
    print_color "$RED" "Error: You must enable at least one platform."
    exit 1
fi

# NapCat (QQ) Configuration
if [[ "$disable_qq" != "true" ]]; then
    print_header "NapCat (QQ) Configuration"
    napcat_url=$(ask_config "Enter NapCat WebSocket URL" "$napcat_url" "false")
    napcat_token=$(ask_config "Enter NapCat API token" "$napcat_token" "true")
    napcat_group_id=$(ask_config "Enter QQ group ID" "$napcat_group_id" "false")
fi

# Discord Configuration
if [[ "$disable_discord" != "true" ]]; then
    print_header "Discord Configuration"
    print_color "$NC" "You can use either a webhook URL or a bot token with guild and channel IDs."

    read -p "Do you want to use a webhook? (Y/N, default: Y): " use_webhook
    use_webhook=${use_webhook:-Y}

    if [[ "$use_webhook" == "Y" || "$use_webhook" == "y" ]]; then
        discord_webhook_url=$(ask_config "Enter Discord webhook URL" "$discord_webhook_url" "true")
        discord_auto_webhook="false"  # No need for auto-webhook if URL is provided
    else
        discord_bot_token=$(ask_config "Enter Discord bot token" "$discord_bot_token" "true")
        discord_guild_id=$(ask_config "Enter Discord guild (server) ID" "$discord_guild_id" "false")
        discord_channel_id=$(ask_config "Enter Discord channel ID" "$discord_channel_id" "false")

        read -p "Do you want to enable auto-webhook creation? (Y/N, default: Y): " auto_webhook
        auto_webhook=${auto_webhook:-Y}
        if [[ "$auto_webhook" == "Y" || "$auto_webhook" == "y" ]]; then
            discord_auto_webhook="true"
            discord_webhook_name=$(ask_config "Enter webhook name" "$discord_webhook_name" "false")
        else
            discord_auto_webhook="false"
        fi
    fi

    read -p "Do you want to use a proxy for Discord API calls? (Y/N, default: Y): " use_proxy
    use_proxy=${use_proxy:-Y}
    if [[ "$use_proxy" == "Y" || "$use_proxy" == "y" ]]; then
        discord_use_proxy="true"
    else
        discord_use_proxy="false"
    fi
fi

# Telegram Configuration
if [[ "$disable_telegram" != "true" ]]; then
    print_header "Telegram Configuration"
    telegram_bot_token=$(ask_config "Enter Telegram bot token" "$telegram_bot_token" "true")
    telegram_chat_id=$(ask_config "Enter Telegram chat ID" "$telegram_chat_id" "false")
    
    read -p "Do you want to use a webhook for Telegram? (Y/N, default: N): " use_webhook
    if [[ "$use_webhook" == "Y" || "$use_webhook" == "y" ]]; then
        print_color "$YELLOW" "Note: Telegram webhooks require a publicly accessible HTTPS server."
        telegram_webhook_url=$(ask_config "Enter Telegram webhook URL (e.g., https://your-domain.com/telegram-webhook)" "$telegram_webhook_url" "true")
    fi
fi

# Debug Configuration
print_header "Debug Configuration"
read -p "Do you want to show NapCat API responses? (Y/N, default: N): " show_responses
if [[ "$show_responses" == "Y" || "$show_responses" == "y" ]]; then
    show_napcat_response="true"
else
    show_napcat_response="false"
fi

read -p "Do you want to enable the debug shell? (Y/N, default: N): " debug_shell
if [[ "$debug_shell" == "Y" || "$debug_shell" == "y" ]]; then
    enable_shell="true"
else
    enable_shell="false"
fi

# Create the configuration file
print_color "$NC" "Creating configuration file..."
cat > "$config_path" << EOF
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
    "WebhookUrl": "$telegram_webhook_url"
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
EOF
print_color "$GREEN" "Configuration file created at: $config_path"

# Display completion message
print_header "Configuration Complete"
print_color "$GREEN" "DC.QQ.TG has been successfully configured!"
print_color "$GREEN" "You can now run the application to use your new configuration."
