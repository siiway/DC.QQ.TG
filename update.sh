#!/bin/bash
#
# DC.QQ.TG - Cross-Platform Messaging Update Script for Linux/macOS
# This script automates the update process for the DC.QQ.TG application
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
print_color "$GREEN" "DC.QQ.TG - Cross-Platform Messaging Update Script"
print_color "$NC" "This script will update the DC.QQ.TG application to the latest version."
print_color "$YELLOW" "Press Ctrl+C at any time to cancel the update."
echo ""

# Check if running as root
if [[ $EUID -eq 0 ]]; then
    print_color "$YELLOW" "Warning: This script is running as root."
    print_color "$YELLOW" "It's recommended to run it as a regular user with sudo privileges."
    echo ""
    read -p "Do you want to continue anyway? (Y/N): " continue_as_root
    if [[ "$continue_as_root" != "Y" && "$continue_as_root" != "y" ]]; then
        print_color "$RED" "Update cancelled."
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

# Determine installation directory
print_header "Installation Directory"
default_install_dir="$HOME/DC.QQ.TG"
read -p "Enter installation directory (default: $default_install_dir): " install_dir
install_dir=${install_dir:-$default_install_dir}

# Check if the directory exists
if [ ! -d "$install_dir" ]; then
    print_color "$RED" "Error: Directory $install_dir does not exist."
    print_color "$RED" "Please run the installation script first."
    exit 1
fi

# Check if it's a git repository
if [ ! -d "$install_dir/.git" ]; then
    print_color "$RED" "Error: $install_dir is not a git repository."
    print_color "$RED" "Please run the installation script first."
    exit 1
fi

# Change to installation directory
cd "$install_dir"
print_color "$GREEN" "Installation directory: $install_dir"

# Backup configuration file
print_header "Backing Up Configuration"
config_path="$install_dir/publish/appsettings.json"
backup_path="$install_dir/publish/appsettings.backup.json"

if [ -f "$config_path" ]; then
    print_color "$NC" "Backing up configuration file..."
    cp "$config_path" "$backup_path"
    print_color "$GREEN" "Configuration backed up to: $backup_path"
else
    print_color "$YELLOW" "Warning: Configuration file not found at $config_path"
    print_color "$YELLOW" "A new configuration file will be created after the update."
fi

# Update the repository
print_header "Updating Repository"
print_color "$NC" "Pulling latest changes from the repository..."
git fetch
current_branch=$(git rev-parse --abbrev-ref HEAD)
git pull origin $current_branch

if [ $? -ne 0 ]; then
    print_color "$RED" "Error: Failed to pull latest changes."
    print_color "$RED" "Please resolve any conflicts and try again."
    exit 1
fi

print_color "$GREEN" "Repository updated successfully."

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
    print_color "$RED" ".NET SDK is not installed or not in PATH."
    print_color "$RED" "Please install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
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

# Restore configuration file
print_header "Restoring Configuration"
if [ -f "$backup_path" ]; then
    print_color "$NC" "Restoring configuration file..."
    cp "$backup_path" "$config_path"
    print_color "$GREEN" "Configuration restored from: $backup_path"
else
    print_color "$YELLOW" "Warning: No backup configuration file found at $backup_path"
    print_color "$YELLOW" "You may need to reconfigure the application."
fi

# Update desktop shortcut (for Linux with desktop environments)
if [[ "$OS" == "linux" && -d "$HOME/.local/share/applications" ]]; then
    print_header "Updating Desktop Shortcut"
    desktop_file="$HOME/.local/share/applications/dc-qq-tg.desktop"
    
    if [ -f "$desktop_file" ]; then
        print_color "$NC" "Updating desktop shortcut..."
    else
        print_color "$NC" "Creating desktop shortcut..."
    fi
    
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
    
    print_color "$GREEN" "Desktop shortcut updated at: $desktop_file"
fi

# Update symbolic link
if [ -L "/usr/local/bin/dc-qq-tg" ]; then
    print_header "Updating Symbolic Link"
    print_color "$NC" "Updating symbolic link..."
    sudo ln -sf "$install_dir/publish/DC.QQ.TG" /usr/local/bin/dc-qq-tg
    print_color "$GREEN" "Symbolic link updated: /usr/local/bin/dc-qq-tg"
fi

# Display completion message
print_header "Update Complete"
print_color "$GREEN" "DC.QQ.TG has been successfully updated!"
print_color "$NC" "Installation directory: $install_dir"
print_color "$NC" "Executable location: $install_dir/publish/DC.QQ.TG"

print_color "$NC" ""
print_color "$NC" "You can run the application by:"
if [ -L "/usr/local/bin/dc-qq-tg" ]; then
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
print_color "$GREEN" "Thank you for updating DC.QQ.TG!"
