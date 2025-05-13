# DC.QQ.TG Installation Guide

This document provides instructions for installing the DC.QQ.TG cross-platform messaging application using the provided installation scripts.

## One-Click Installation

We provide installation scripts for both Windows and Linux/macOS systems to simplify the setup process.

### Prerequisites

- **Windows**: PowerShell 5.1 or later
- **Linux/macOS**: Bash shell
- Internet connection to download dependencies
- Git (will be installed by the script if not present)
- Administrator/sudo privileges (recommended but not required)

### Windows Installation

1. Download the `install.ps1` script
2. Right-click on the script and select "Run with PowerShell"
   - If you get a security warning, you may need to run `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` first
3. Follow the on-screen instructions
4. The script will:
   - Check for and install .NET 9 SDK if needed
   - Clone the repository
   - Build the application
   - Create a sample configuration file
   - Create a desktop shortcut

```powershell
# Alternative: Run from PowerShell
.\install.ps1
```

### Linux/macOS Installation

1. Download the `install.sh` script
2. Open a terminal and navigate to the directory containing the script
3. Make the script executable: `chmod +x install.sh`
4. Run the script: `./install.sh`
5. Follow the on-screen instructions
6. The script will:
   - Check for and install .NET 9 SDK if needed
   - Clone the repository
   - Build the application
   - Create a sample configuration file
   - Create a desktop shortcut (Linux only)
   - Optionally create a symbolic link in /usr/local/bin

```bash
# Download and run in one command
curl -sSL https://raw.githubusercontent.com/siiway/DC.QQ.TG/main/install.sh | bash
```

## Manual Installation

If you prefer to install the application manually, follow these steps:

### Windows

1. Install .NET 9 SDK from [Microsoft's website](https://dotnet.microsoft.com/download/dotnet/9.0)
2. Clone the repository:

   ```bash
   git clone https://github.com/siiway/DC.QQ.TG.git
   cd DC.QQ.TG
   ```

3. Build the application:

   ```bash
   dotnet restore
   dotnet publish -c Release -r win-x64 -o ./publish --self-contained
   ```

4. Configure the application by editing `publish/appsettings.json`
5. Run the application:

   ```bash
   cd publish
   .\DC.QQ.TG.exe
   ```

### Linux/macOS

1. Install .NET 9 SDK:
   - **Ubuntu/Debian**:

     ```bash
     wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
     sudo dpkg -i packages-microsoft-prod.deb
     sudo apt-get update
     sudo apt-get install -y dotnet-sdk-9.0
     ```

   - **Fedora/RHEL**:

     ```bash
     sudo dnf install dotnet-sdk-9.0
     ```

   - **macOS**:

     ```bash
     brew install --cask dotnet-sdk
     ```

2. Clone the repository:

   ```bash
   git clone https://github.com/siiway/DC.QQ.TG.git
   cd DC.QQ.TG
   ```

3. Build the application:

   ```bash
   dotnet restore
   # For Linux
   dotnet publish -c Release -r linux-x64 -o ./publish --self-contained
   # For macOS
   dotnet publish -c Release -r osx-x64 -o ./publish --self-contained
   ```

4. Make the executable file executable:

   ```bash
   chmod +x ./publish/DC.QQ.TG
   ```

5. Configure the application by editing `publish/appsettings.json`
6. Run the application:

   ```bash
   cd publish
   ./DC.QQ.TG
   ```

## Configuration

During installation, the script will ask if you want to configure the application interactively. If you choose to do so, you'll be guided through the configuration process with prompts for each setting.

Alternatively, you can configure the application later by editing the `appsettings.json` file in the publish directory. The following settings are available:

### NapCat (QQ) Configuration

- `BaseUrl`: The WebSocket URL for NapCat (e.g., `ws://localhost:3001`)
- `Token`: Your NapCat API token
- `GroupId`: Your QQ group ID

### Discord Configuration

- `WebhookUrl`: Your Discord webhook URL
- `BotToken`: Your Discord bot token
- `GuildId`: Your Discord guild (server) ID
- `ChannelId`: Your Discord channel ID
- `UseProxy`: Whether to use a proxy for Discord API calls (`true` or `false`)
- `AutoWebhook`: Whether to automatically create webhooks (`true` or `false`)
- `WebhookName`: The name for the webhook

### Telegram Configuration

- `BotToken`: Your Telegram bot token
- `ChatId`: Your Telegram chat ID

### Debug Configuration

- `ShowNapCatResponse`: Whether to show NapCat API responses (`true` or `false`)
- `EnableShell`: Whether to enable the debug shell (`true` or `false`)

## Running the Application

### Using Configuration File

If you've configured the application during installation or edited the `appsettings.json` file manually, you can simply run the application without any command line arguments:

```bash
# Windows
.\DC.QQ.TG.exe

# Linux/macOS
./DC.QQ.TG
```

### Using Command Line Arguments

Alternatively, you can run the application using command line arguments to specify the configuration:

```bash
# Windows
.\DC.QQ.TG.exe --discord-webhook-url=<url> --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>

# Linux/macOS
./DC.QQ.TG --discord-webhook-url=<url> --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>
```

You can disable specific platforms if you don't want to use them:

```bash
# Disable Telegram
./DC.QQ.TG --disable-telegram=true --discord-webhook-url=<url> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>

# Disable Discord
./DC.QQ.TG --disable-discord=true --telegram-bot-token=<token> --telegram-chat-id=<chat_id> --napcat-url=<url> --napcat-token=<token> --qq-group=<group_id>

# Disable QQ
./DC.QQ.TG --disable-qq=true --discord-webhook-url=<url> --telegram-bot-token=<token> --telegram-chat-id=<chat_id>
```

### Debug Options

You can enable additional debug features:

```bash
# Show NapCat API responses
./DC.QQ.TG --show-napcat-response=true

# Enable debug shell
./DC.QQ.TG --debug-shell=true
```

## Troubleshooting

If you encounter issues during installation or running the application:

1. Make sure you have the correct version of .NET SDK installed
2. Check that all required API keys and tokens are valid
3. Verify that the configuration file has the correct format
4. Check the console output for error messages
5. Try running with the `--debug-shell` option for more detailed logging

For more help, please open an issue on the [GitHub repository](https://github.com/siiway/DC.QQ.TG/issues).
