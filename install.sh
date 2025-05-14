#!/bin/bash
#
# DC.QQ.TG - Cross-Platform Messaging Installation Script for Linux/macOS
# This script automates the installation process for the DC.QQ.TG application
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
print_color "$GREEN" "DC.QQ.TG - Cross-Platform Messaging Installation Script"
print_color "$NC" "This script will install the DC.QQ.TG application on your system."
print_color "$YELLOW" "Press Ctrl+C at any time to cancel the installation."
echo ""

# Check if running as root
if [[ $EUID -eq 0 ]]; then
    print_color "$YELLOW" "Warning: This script is running as root."
    print_color "$YELLOW" "It's recommended to run it as a regular user with sudo privileges."
    echo ""
    read -p "Do you want to continue anyway? (Y/N): " continue_as_root
    if [[ "$continue_as_root" != "Y" && "$continue_as_root" != "y" ]]; then
        print_color "$RED" "Installation cancelled."
        exit 1
    fi
fi

# Detect OS
OS=$(detect_os)
if [[ "$OS" == "unknown" ]]; then
    print_color "$RED" "Unsupported operating system. This script supports Linux and macOS only."
    exit 1
fi

print_header "System Information"
print_color "$NC" "Operating System: $OS"

# Check if .NET 9 SDK is installed
print_header "Checking .NET SDK"
if command_exists dotnet; then
    dotnet_version=$(dotnet --version)
    if [[ "$dotnet_version" == 9.* ]]; then
        print_color "$GREEN" ".NET 9 SDK is already installed: $dotnet_version"
    else
        print_color "$YELLOW" ".NET 9 SDK is not installed. Current version: $dotnet_version"
        print_color "$YELLOW" "Installing .NET 9 SDK..."

        if [[ "$OS" == "linux" ]]; then
            # Install .NET 9 SDK on Linux
            print_color "$NC" "Downloading .NET 9 SDK installer for Linux..."

            # Check if we're on a Debian/Ubuntu-based system
            if command_exists apt-get; then
                print_color "$NC" "Detected Debian/Ubuntu-based system"

                # Add Microsoft package repository
                wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
                sudo dpkg -i packages-microsoft-prod.deb
                rm packages-microsoft-prod.deb

                # Install .NET 9 SDK
                sudo apt-get update
                sudo apt-get install -y dotnet-sdk-9.0
            # Check if we're on a RHEL/Fedora-based system
            elif command_exists dnf; then
                print_color "$NC" "Detected RHEL/Fedora-based system"

                # Add Microsoft package repository
                sudo dnf install -y dotnet-sdk-9.0
            else
                print_color "$RED" "Unsupported Linux distribution. Please install .NET 9 SDK manually from https://dotnet.microsoft.com/download/dotnet/9.0"
                exit 1
            fi
        elif [[ "$OS" == "macos" ]]; then
            # Install .NET 9 SDK on macOS using Homebrew
            if command_exists brew; then
                print_color "$NC" "Installing .NET 9 SDK using Homebrew..."
                brew install --cask dotnet-sdk
            else
                print_color "$YELLOW" "Homebrew not found. Installing Homebrew first..."
                /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
                print_color "$NC" "Installing .NET 9 SDK using Homebrew..."
                brew install --cask dotnet-sdk
            fi
        fi

        # Verify installation
        if command_exists dotnet; then
            new_dotnet_version=$(dotnet --version)
            if [[ "$new_dotnet_version" == 9.* ]]; then
                print_color "$GREEN" ".NET 9 SDK installed successfully: $new_dotnet_version"
            else
                print_color "$RED" "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0"
                exit 1
            fi
        else
            print_color "$RED" "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0"
            exit 1
        fi
    fi
else
    print_color "$YELLOW" ".NET SDK is not installed or not in PATH."
    print_color "$YELLOW" "Installing .NET 9 SDK automatically..."

    if [[ "$OS" == "linux" ]]; then
        # Install .NET 9 SDK on Linux
        print_color "$NC" "Downloading .NET 9 SDK installer for Linux..."

        # Check if we're on a Debian/Ubuntu-based system
        if command_exists apt-get; then
            print_color "$NC" "Detected Debian/Ubuntu-based system"

            # Add Microsoft package repository
            wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
            sudo dpkg -i packages-microsoft-prod.deb
            rm packages-microsoft-prod.deb

            # Install .NET 9 SDK
            sudo apt-get update
            sudo apt-get install -y dotnet-sdk-9.0
        # Check if we're on a RHEL/Fedora-based system
        elif command_exists dnf; then
            print_color "$NC" "Detected RHEL/Fedora-based system"

            # Add Microsoft package repository
            sudo dnf install -y dotnet-sdk-9.0
        else
            print_color "$RED" "Unsupported Linux distribution. Please install .NET 9 SDK manually from https://dotnet.microsoft.com/download/dotnet/9.0"
            exit 1
        fi
    elif [[ "$OS" == "macos" ]]; then
        # Install .NET 9 SDK on macOS using Homebrew
        if command_exists brew; then
            print_color "$NC" "Installing .NET 9 SDK using Homebrew..."
            brew install --cask dotnet-sdk
        else
            print_color "$YELLOW" "Homebrew not found. Installing Homebrew first..."
            /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
            print_color "$NC" "Installing .NET 9 SDK using Homebrew..."
            brew install --cask dotnet-sdk
        fi
    fi

    # Verify installation
    if command_exists dotnet; then
        new_dotnet_version=$(dotnet --version)
        if [[ "$new_dotnet_version" == 9.* ]]; then
            print_color "$GREEN" ".NET 9 SDK installed successfully: $new_dotnet_version"
        else
            print_color "$RED" "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0"
            exit 1
        fi
    else
        print_color "$RED" "Failed to install .NET 9 SDK. Please install it manually from https://dotnet.microsoft.com/download/dotnet/9.0"
        exit 1
    fi
fi

# Check if Git is installed
print_header "Checking Git"
if command_exists git; then
    git_version=$(git --version)
    print_color "$GREEN" "Git is installed: $git_version"
else
    print_color "$RED" "Git is not installed. Please install Git before continuing."
    exit 1
fi

# Determine installation directory
print_header "Installation Directory"
default_install_dir="$HOME/DC.QQ.TG"
read -p "Enter installation directory (default: $default_install_dir): " install_dir
install_dir=${install_dir:-$default_install_dir}

# Create installation directory if it doesn't exist
if [ ! -d "$install_dir" ]; then
    print_color "$NC" "Creating directory: $install_dir"
    mkdir -p "$install_dir"
fi

# Change to installation directory
cd "$install_dir"
print_color "$GREEN" "Installation directory: $install_dir"

# Check if repository is already cloned
if [ -d "$install_dir/.git" ]; then
    print_header "Updating Repository"
    print_color "$NC" "Repository already exists. Pulling latest changes..."
    git pull
else
    print_header "Cloning Repository"
    print_color "$NC" "Cloning DC.QQ.TG repository..."
    git clone https://github.com/siiway/DC.QQ.TG.git .
fi

# Build the application
print_header "Building Application"
print_color "$NC" "Restoring dependencies..."
dotnet restore

print_color "$NC" "Building application..."
if [[ "$OS" == "linux" ]]; then
    dotnet publish -c Release -r linux-x64 -o ./publish --self-contained
elif [[ "$OS" == "macos" ]]; then
    dotnet publish -c Release -r osx-x64 -o ./publish --self-contained
fi

if [ -f "$install_dir/publish/DC.QQ.TG" ]; then
    print_color "$GREEN" "Application built successfully!"
    # Make the executable file executable
    chmod +x "$install_dir/publish/DC.QQ.TG"
else
    print_color "$RED" "Failed to build the application."
    exit 1
fi

# Configuration setup
print_header "Configuration Setup"
config_path="$install_dir/publish/appsettings.json"

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

# Ask if user wants to configure the application now
print_color "$NC" "Would you like to configure the application now?"
read -p "This will ask for your API keys and settings (Y/N, default: Y): " configure_now
configure_now=${configure_now:-Y}

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

if [[ "$configure_now" == "Y" || "$configure_now" == "y" ]]; then
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
        napcat_token=$(ask_config "Enter NapCat API token" "" "true")
        napcat_group_id=$(ask_config "Enter QQ group ID" "" "false")
    fi

    # Discord Configuration
    if [[ "$disable_discord" != "true" ]]; then
        print_header "Discord Configuration"
        print_color "$NC" "You can use either a webhook URL or a bot token with guild and channel IDs."

        read -p "Do you want to use a webhook? (Y/N, default: Y): " use_webhook
        use_webhook=${use_webhook:-Y}

        if [[ "$use_webhook" == "Y" || "$use_webhook" == "y" ]]; then
            discord_webhook_url=$(ask_config "Enter Discord webhook URL" "" "true")
            discord_auto_webhook="false"  # No need for auto-webhook if URL is provided
        else
            discord_bot_token=$(ask_config "Enter Discord bot token" "" "true")
            discord_guild_id=$(ask_config "Enter Discord guild (server) ID" "" "false")
            discord_channel_id=$(ask_config "Enter Discord channel ID" "" "false")

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
        telegram_bot_token=$(ask_config "Enter Telegram bot token" "" "true")
        telegram_chat_id=$(ask_config "Enter Telegram chat ID" "" "false")

        read -p "Do you want to use a webhook for Telegram? (Y/N, default: N): " use_webhook
        if [[ "$use_webhook" == "Y" || "$use_webhook" == "y" ]]; then
            print_color "$YELLOW" "Note: Telegram webhooks require a publicly accessible HTTPS server."
            telegram_webhook_url=$(ask_config "Enter Telegram webhook URL (e.g., https://your-domain.com/telegram-webhook)" "" "true")
        fi
    fi

    # Debug Configuration
    print_header "Debug Configuration"
    read -p "Do you want to show NapCat API responses? (Y/N, default: N): " show_responses
    if [[ "$show_responses" == "Y" || "$show_responses" == "y" ]]; then
        show_napcat_response="true"
    fi

    read -p "Do you want to enable the debug shell? (Y/N, default: N): " debug_shell
    if [[ "$debug_shell" == "Y" || "$debug_shell" == "y" ]]; then
        enable_shell="true"
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
else
    # Create default configuration file
    print_color "$NC" "Creating default configuration file..."
    cat > "$config_path" << EOF
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
    "WebhookUrl": "https://your-domain.com/telegram-webhook"
  },
  "Debug": {
    "ShowNapCatResponse": false,
    "EnableShell": false
  }
}
EOF
    print_color "$GREEN" "Default configuration file created at: $config_path"
    print_color "$YELLOW" "Please edit this file to add your API keys and settings."
fi

# Create a desktop shortcut (for Linux with desktop environments)
if [[ "$OS" == "linux" && -d "$HOME/.local/share/applications" ]]; then
    print_header "Creating Desktop Shortcut"
    desktop_file="$HOME/.local/share/applications/dc-qq-tg.desktop"

    cat > "$desktop_file" << EOF
[Desktop Entry]
Type=Application
Name=DC.QQ.TG
Comment=Cross-Platform Messaging
Exec=$install_dir/publish/DC.QQ.TG
Icon=
Terminal=true
Categories=Utility;
EOF

    print_color "$GREEN" "Desktop shortcut created at: $desktop_file"
fi

# Create a symbolic link to the executable in /usr/local/bin (requires sudo)
print_header "Creating Symbolic Link"
read -p "Do you want to create a symbolic link in /usr/local/bin? (Y/N): " create_symlink
if [[ "$create_symlink" == "Y" || "$create_symlink" == "y" ]]; then
    sudo ln -sf "$install_dir/publish/DC.QQ.TG" /usr/local/bin/dc-qq-tg
    print_color "$GREEN" "Symbolic link created: /usr/local/bin/dc-qq-tg"
    print_color "$NC" "You can now run the application from anywhere using the command: dc-qq-tg"
fi

# Display completion message
print_header "Installation Complete"
print_color "$GREEN" "DC.QQ.TG has been successfully installed!"
print_color "$NC" "Installation directory: $install_dir"
print_color "$NC" "Executable location: $install_dir/publish/DC.QQ.TG"

if [[ "$configure_now" == "Y" || "$configure_now" == "y" ]]; then
    print_color "$GREEN" ""
    print_color "$GREEN" "Your application has been configured and is ready to use!"
else
    print_color "$YELLOW" ""
    print_color "$YELLOW" "Before running the application, make sure to:"
    print_color "$YELLOW" "1. Edit the configuration file at: $config_path"
    print_color "$YELLOW" "2. Set up your API keys for the platforms you want to use"
fi

print_color "$NC" ""
print_color "$NC" "You can run the application by:"
if [[ "$create_symlink" == "Y" || "$create_symlink" == "y" ]]; then
    print_color "$NC" "- Using the command: dc-qq-tg"
fi
if [[ "$OS" == "linux" && -d "$HOME/.local/share/applications" ]]; then
    print_color "$NC" "- Using the desktop shortcut"
fi
print_color "$NC" "- Running the executable directly"
print_color "$NC" "- Using command line with parameters:"
print_color "$CYAN" "  cd $install_dir/publish"
print_color "$CYAN" "  ./DC.QQ.TG"

print_color "$GREEN" ""
print_color "$GREEN" "Thank you for installing DC.QQ.TG!"
