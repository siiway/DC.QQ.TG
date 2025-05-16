# DC.QQ.TG - Cross-Platform Messaging

A C# console application that enables cross-platform messaging between Discord, QQ, and Telegram.

## Features

- Bidirectional message synchronization between Discord, QQ, and Telegram
- Support for text messages, images, and files
- User avatars and formatted usernames across platforms
- WebSocket and HTTP support for QQ integration
- Discord Bot API and Webhook support
- One-click installation scripts for Windows, Linux, and macOS
- One-click update scripts for easy maintenance
- Interactive configuration wizard during installation
- Configurable through appsettings.json or command line arguments
- Interactive debug shell for testing and diagnostics
- Runs as a background service

## Prerequisites

- .NET 9.0 or later
- NapCat for QQ integration
- Discord Bot Token or Webhook URL
- Telegram Bot Token

## Setup

### One-Click Installation

We provide installation scripts for both Windows and Linux/macOS systems to simplify the setup process:

#### Windows

1. Download the `install.bat` or `install.ps1` script
2. Run the script by double-clicking `install.bat` or right-clicking `install.ps1` and selecting "Run with PowerShell"
3. Follow the on-screen instructions
4. The script will:
   - Check for and install .NET 9 SDK if needed
   - Clone the repository
   - Build the application
   - Guide you through the configuration process
   - Create a desktop shortcut

#### Linux/macOS

1. Download the `install.sh` script
2. Make it executable: `chmod +x install.sh`
3. Run the script: `./install.sh`
4. Follow the on-screen instructions
5. The script will:
   - Check for and install .NET 9 SDK if needed
   - Clone the repository
   - Build the application
   - Guide you through the configuration process
   - Create a desktop shortcut (Linux only)
   - Optionally create a symbolic link in /usr/local/bin

For more detailed installation instructions, see [INSTALL.md](INSTALL.md).

### One-Click Update

We provide update scripts for both Windows and Linux/macOS systems to simplify the update process:

#### Windows Update

1. Download the `update.bat` or `update.ps1` script to your DC.QQ.TG installation directory
2. Run the script by double-clicking `update.bat` or right-clicking `update.ps1` and selecting "Run with PowerShell"
3. Follow the on-screen instructions
4. The script will:
   - Backup your configuration
   - Pull the latest changes from the repository
   - Check for and install .NET 9 SDK if needed
   - Build the application
   - Restore your configuration
   - Update the desktop shortcut

#### Linux/macOS Update

1. Download the `update.sh` script to your DC.QQ.TG installation directory
2. Make it executable: `chmod +x update.sh`
3. Run the script: `./update.sh`
4. Follow the on-screen instructions
5. The script will:
   - Backup your configuration
   - Pull the latest changes from the repository
   - Check for and install .NET 9 SDK if needed
   - Build the application
   - Restore your configuration
   - Update the desktop shortcut (Linux only)
   - Update the symbolic link (if it exists)

For more detailed update instructions, see [UPDATE.md](UPDATE.md).

### Manual Setup

1. Clone the repository
2. Configure the application by editing `appsettings.json`:

```json
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
    "ChannelId": "your_discord_channel_id_here"
  },
  "Telegram": {
    "BotToken": "your_telegram_bot_token_here",
    "ChatId": "your_telegram_chat_id_here",
    "WebhookUrl": "https://your-domain.com:8443",
    "WebhookPort": "8443",
    "CertificatePath": "path/to/your/certificate.pem",
    "CertificatePassword": "certificate_password_if_needed"
  },
  "Debug": {
    "ShowNapCatResponse": false,
    "EnableShell": false
  }
}
```

### NapCat Setup

1. Install and set up NapCat according to the [official documentation](https://napcat.apifox.cn)
2. Get your API token and set it in the configuration
3. Get your QQ group ID and set it in the configuration
4. You can use either WebSocket (ws://) or HTTP protocol for NapCat

> [!NOTE]
> HTTP is **NOT** recommended for NapCat integration.
> Please use WebSocket instead.

#### NapCat Media and File Support

The application uses NapCat API to send images and files directly to QQ:

- **Images**: Images from Discord and Telegram are sent to QQ using the NapCat API's image message type
- **Files**: Files from Discord and Telegram are sent to QQ using the NapCat API's file message type
- Supported file sources include:
  - Network URLs (http:// or https://)
  - Local file paths (file://)
  - Base64 encoded data
- File types are automatically detected based on file extensions

### Discord Setup

You can use either Discord Bot API or Webhook for Discord integration:

#### Discord Bot API (Recommended for bidirectional messaging)

1. Create a Discord application at [Discord Developer Portal](https://discord.com/developers/applications)
2. Create a bot for your application
3. Enable the following Intents under the Bot settings:
   - MESSAGE CONTENT INTENT
   - SERVER MEMBERS INTENT
   - PRESENCE INTENT
4. Copy the bot token and set it in the configuration
5. Invite the bot to your server using the OAuth2 URL generator with the following permissions:
   - Read Messages/View Channels
   - Send Messages
   - Embed Links
   - Attach Files
   - Read Message History
   - Use External Emojis
   - Add Reactions
   - Manage Webhooks
6. Get your Guild ID (server ID) and Channel ID and set them in the configuration

#### Discord Webhook (One-way messaging to Discord only)

1. Create a Discord server or use an existing one
2. You can either:
   - **Manually create a webhook in the correct channel**:
     - Go to Server Settings > Integrations > Webhooks
     - Click "New Webhook"
     - Select the channel where you want to receive messages
     - Copy the webhook URL and set it in the configuration
   - **Let the application manage webhooks automatically**:
     - Set up the Discord Bot API as described above
     - Set `--auto-webhook=true` in the command line arguments
     - Optionally set `--discord-webhook-name` to customize the webhook name
     - The app will create a webhook or move an existing one to the correct channel

> [!NOTE]
> If you use `dotnet run` to run the application, Auto-Webhook feature is enabled by default. If you provide a webhook URL in the configuration, it will be used and auto-webhook will be disabled.

### Telegram Setup

1. Create a new bot using BotFather:
   - Start a chat with [@BotFather](https://t.me/BotFather)
   - Send `/newbot` and follow the instructions
   - Copy the bot token and set it in the configuration
2. Get your chat ID:
   - Add the bot to a group or start a private chat with it
   - Send a message to the bot
   - Visit `https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates`
   - Find the `chat` object and copy the `id` value
3. (Optional) Set up a webhook for real-time message delivery:
   - Set up a publicly accessible HTTPS server (required for webhooks)
   - Configure your domain to point to your server
   - Set the webhook URL in the configuration (e.g., `https://your-domain.com:8443`)
   - **Important**: The webhook URL must use HTTPS (Telegram requirement)
   - The path part of the URL is optional and will be used as the webhook endpoint
   - Optionally set a custom port for the webhook listener (default: 8443)
   - Telegram only allows webhooks on ports 443, 80, 88, and 8443
   - (Optional) Provide a path to your SSL certificate file (PEM format) in `CertificatePath`
   - (Optional) If your certificate is password-protected, provide the password in `CertificatePassword`
   - The application will automatically set up the webhook with Telegram
   - This provides faster and more reliable message delivery than polling

## Running the Application

You can run the application using command line arguments to specify the configuration:

```bash
cd DC.QQ.TG
dotnet run -- --discord-webhook-url=<webhook_url> --discord-bot-token=<bot_token> --discord-guild-id=<guild_id> --discord-channel-id=<channel_id> --telegram-bot-token=<bot_token> --telegram-chat-id=<chat_id> --napcat-url=<napcat_url> --napcat-token=<napcat_token> --qq-group=<group_id>
```

All parameters are required for their respective platforms. The application will validate the API keys and connections before starting.

### Command Line Arguments

#### NapCat (QQ) Parameters

- `--napcat-url`: The URL of the NapCat API (can be HTTP or WebSocket)
- `--napcat-token`: Your NapCat API token
- `--qq-group`: The QQ group ID to send and receive messages from

#### Discord Parameters

- `--discord-webhook-url`: Your Discord webhook URL (for sending messages to Discord)
- `--discord-bot-token`: Your Discord bot token (for bidirectional messaging)
- `--discord-guild-id`: Your Discord server (guild) ID
- `--discord-channel-id`: Your Discord channel ID
- `--discord-use-proxy`: Whether to use system proxy for Discord connection (true/false)
- `--auto-webhook`: Whether to automatically manage webhooks (create or move to correct channel) (true/false, default: true, automatically disabled if webhook URL is provided)
- `--discord-webhook-name`: The name for the automatically managed webhook (default: "Cross-Platform Messenger")

#### Telegram Parameters

- `--telegram-bot-token`: Your Telegram bot token
- `--telegram-chat-id`: Your Telegram chat ID
- `--telegram-webhook-url`: (Optional) Your Telegram webhook URL for real-time message delivery (e.g., `https://your-domain.com:8443`)
- `--telegram-webhook-port`: (Optional) Custom port for Telegram webhook listener (default: 8443)
- `--telegram-certificate-path`: (Optional) Path to your SSL certificate file for Telegram webhook
- `--telegram-certificate-password`: (Optional) Password for your SSL certificate if it's password-protected

#### Debug Parameters

- `--show-napcat-response`: Show detailed NapCat API responses for debugging
- `--debug-shell`: Enable an interactive debug shell for testing and diagnostics

#### Platform Control

- `--disable-qq`: Disable QQ integration
- `--disable-discord`: Disable Discord integration
- `--disable-telegram`: Disable Telegram integration

### Interactive Configuration

During installation, the script will guide you through the configuration process with interactive prompts:

1. **Platform Selection**: Choose which platforms you want to enable (QQ, Discord, Telegram)
2. **NapCat (QQ) Configuration**: Enter your NapCat WebSocket URL, API token, and QQ group ID
3. **Discord Configuration**: Choose between webhook or bot token, and enter the necessary credentials
4. **Telegram Configuration**: Enter your Telegram bot token and chat ID
5. **Debug Options**: Configure debug settings like showing NapCat API responses and enabling the debug shell

This interactive configuration makes it easy to set up the application without manually editing configuration files.

### Debug Shell

When the debug shell is enabled, you can use the following commands:

- `help`: Show help message
- `exit`, `quit`: Exit the application
- `status`: Show current status of all platforms
- `adapters`: List all registered adapters
- `send`: Send a test message to all platforms
- `send <message>`: Send a custom message to all platforms
- `vars`, `variables`, `get`: Show all variables
- `get <name>`: Show the value of a specific variable
- `set <name> <value>`: Set the value of a variable and save it to appsettings.json
- `messages`, `msgs`: Show the 10 most recent messages
- `get messages <count>`: Show the specified number of recent messages
- `tgmsgs`, `telegram`: Get Telegram chat info and debug information
- `tgwebhook`, `test-webhook`: Test Telegram webhook connectivity and troubleshoot connection issues

When you use the `set` command to modify a variable, the change is automatically saved to the `appsettings.json` file. This means your changes will persist even after restarting the application. Some configuration changes (like enabling/disabling platforms) require a restart to take effect.

Example:

```bash
debug-shell> get napcat_url
debug-shell> set napcat_url ws://new-url:8086
debug-shell> send Hello from debug shell!
```

### Disabling Platforms

You can disable specific platforms if you don't want to use them or don't have the required API keys:

```bash
# Disable Telegram
dotnet run -- --disable-telegram=true --discord-webhook-url=<webhook_url> --napcat-url=<napcat_url> --napcat-token=<napcat_token> --qq-group=<group_id>

# Disable Discord
dotnet run -- --disable-discord=true --telegram-bot-token=<bot_token> --telegram-chat-id=<chat_id> --napcat-url=<napcat_url> --napcat-token=<napcat_token> --qq-group=<group_id>

# Disable QQ
dotnet run -- --disable-qq=true --discord-webhook-url=<webhook_url> --telegram-bot-token=<bot_token> --telegram-chat-id=<chat_id>
```

At least one platform must be enabled for the application to run.

## Message Formatting

Messages are formatted as `<user>@<platform>: <message>` across all platforms. For example:

- `john@discord: Hello from Discord!`
- `alice@qq: Hello from QQ!`
- `bob@telegram: Hello from Telegram!`

User avatars are also synchronized across platforms when available.

### Media and File Sharing

The application supports sharing media and files across platforms:

#### Images

- Images shared on any platform will be forwarded to all other platforms
- Discord: Images appear as embedded content
- Telegram: Images are sent as photos
- QQ: Images are sent directly using NapCat API

#### Files

- Files shared on any platform will be forwarded to all other platforms
- Supported file types include documents, videos, audio files, and more
- Discord: Files appear as embedded links with file type information
- Telegram: Files are sent using the appropriate method based on file type (document, video, audio)
- QQ: Files are sent directly using NapCat API with proper file names and types

> [!NOTE]
> File sharing capabilities may vary depending on platform limitations and API restrictions.

## Architecture

The application uses a modular architecture with adapters for each platform:

- `QQAdapter`: Connects to NapCat API for QQ integration (supports both WebSocket and HTTP)
- `DiscordAdapter`: Uses Discord Bot API or Webhooks for Discord integration
- `TelegramAdapter`: Uses Telegram Bot API for Telegram integration

The `MessageService` coordinates between the adapters, ensuring messages are properly synchronized across platforms.

## License

GPL-3.0
