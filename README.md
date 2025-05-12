# DC.QQ.TG - Cross-Platform Messaging

A C# console application that enables cross-platform messaging between Discord, QQ, and Telegram.

## Features

- Bidirectional message synchronization between Discord, QQ, and Telegram
- Support for text messages and images
- User avatars and formatted usernames across platforms
- WebSocket and HTTP support for QQ integration
- Discord Bot API and Webhook support
- Configurable through appsettings.json or command line arguments
- Interactive debug shell for testing and diagnostics
- Runs as a background service

## Prerequisites

- .NET 9.0 or later
- NapCat for QQ integration
- Discord Bot Token or Webhook URL
- Telegram Bot Token

## Setup

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
    "ChatId": "your_telegram_chat_id_here"
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
6. Get your Guild ID (server ID) and Channel ID and set them in the configuration

#### Discord Webhook (One-way messaging to Discord only)
1. Create a Discord server or use an existing one
2. Create a webhook in a channel:
   - Go to Server Settings > Integrations > Webhooks
   - Click "New Webhook"
   - Copy the webhook URL and set it in the configuration

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

## Running the Application

You can run the application using command line arguments to specify the configuration:

```bash
cd DC.QQ.TG
dotnet run -- --discord-webhook-url=<webhook_url> --discord-bot-token=<bot_token> --discord-guild-id=<guild_id> --discord-channel-id=<channel_id> --telegram-bot-token=<bot_token> --telegram-chat-id=<chat_id> --napcat-url=<napcat_url> --napcat-token=<napcat_token> --qq-group=<group_id>
```

All parameters are required for their respective platforms. The application will validate the API keys and connections before starting.

### Command Line Arguments

#### NapCat (QQ) Parameters:
- `--napcat-url`: The URL of the NapCat API (can be HTTP or WebSocket)
- `--napcat-token`: Your NapCat API token
- `--qq-group`: The QQ group ID to send and receive messages from

#### Discord Parameters:
- `--discord-webhook-url`: Your Discord webhook URL (for sending messages to Discord)
- `--discord-bot-token`: Your Discord bot token (for bidirectional messaging)
- `--discord-guild-id`: Your Discord server (guild) ID
- `--discord-channel-id`: Your Discord channel ID
- `--discord-use-proxy`: Whether to use system proxy for Discord connection (true/false)

#### Telegram Parameters:
- `--telegram-bot-token`: Your Telegram bot token
- `--telegram-chat-id`: Your Telegram chat ID

#### Debug Parameters:
- `--show-napcat-response`: Show detailed NapCat API responses for debugging
- `--debug-shell`: Enable an interactive debug shell for testing and diagnostics

#### Platform Control:
- `--disable-qq`: Disable QQ integration
- `--disable-discord`: Disable Discord integration
- `--disable-telegram`: Disable Telegram integration

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
- `set <name> <value>`: Set the value of a variable
- `messages`, `msgs`: Show the 10 most recent messages
- `get messages <count>`: Show the specified number of recent messages

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

## Architecture

The application uses a modular architecture with adapters for each platform:

- `QQAdapter`: Connects to NapCat API for QQ integration (supports both WebSocket and HTTP)
- `DiscordAdapter`: Uses Discord Bot API or Webhooks for Discord integration
- `TelegramAdapter`: Uses Telegram Bot API for Telegram integration

The `MessageService` coordinates between the adapters, ensuring messages are properly synchronized across platforms.

## License

GPL-3.0
