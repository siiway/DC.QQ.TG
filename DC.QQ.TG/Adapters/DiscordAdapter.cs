using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using DC.QQ.TG.Utils;
using Discord;
using Discord.WebSocket;
using MessageSource = DC.QQ.TG.Models.MessageSource;

namespace DC.QQ.TG.Adapters
{
    public class DiscordAdapter : IMessageAdapter, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscordAdapter> _logger;
        private readonly HttpClient _httpClient;
        private string? _webhookUrl;
        private string? _botToken;
        private ulong _guildId;
        private ulong _channelId;
        private DiscordSocketClient? _discordClient;
        private CancellationTokenSource? _cts;
        private readonly bool _isListening;
        private bool _autoWebhook;
        private string _webhookName;

        public MessageSource Platform => MessageSource.Discord;

        public event EventHandler<Message>? MessageReceived;

        public DiscordAdapter(IConfiguration configuration, ILogger<DiscordAdapter> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task InitializeAsync()
        {
            // Get webhook URL for sending messages
            _webhookUrl = _configuration["Discord:WebhookUrl"];

            // Get bot token and channel ID for receiving messages
            _botToken = _configuration["Discord:BotToken"];

            // Get webhook name
            _webhookName = _configuration["Discord:WebhookName"] ?? "Cross-Platform Messenger";

            // Check if we should automatically manage webhooks
            // Default is true, but if webhook URL is already provided, set to false
            bool autoWebhookSetting = _configuration["Discord:AutoWebhook"]?.ToLower() == "true";

            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                // User has provided a webhook URL, so disable auto-webhook
                _autoWebhook = false;
                _logger.LogInformation("Webhook URL provided, disabling auto-webhook feature");
            }
            else
            {
                // Use the setting from configuration
                _autoWebhook = autoWebhookSetting;
                _logger.LogInformation("Auto-webhook is {Status}", _autoWebhook ? "enabled" : "disabled");
            }

            // Try to parse guild ID and channel ID
            if (!ulong.TryParse(_configuration["Discord:GuildId"], out _guildId))
            {
                _logger.LogWarning("Discord guild ID is missing or invalid");
            }

            if (!ulong.TryParse(_configuration["Discord:ChannelId"], out _channelId))
            {
                _logger.LogWarning("Discord channel ID is missing or invalid");
            }

            // Check if we have at least webhook URL or bot token
            if (string.IsNullOrEmpty(_webhookUrl) && string.IsNullOrEmpty(_botToken))
            {
                throw new InvalidOperationException("Discord configuration is missing or invalid. Need either WebhookUrl or BotToken.");
            }

            // Initialize Discord client if bot token is provided
            if (!string.IsNullOrEmpty(_botToken))
            {
                try
                {
                    _logger.LogInformation("Initializing Discord bot client with token: {TokenStart}...",
                        _botToken.Length > 10 ? _botToken.Substring(0, 10) + "..." : "[too short]");

                    // Check if we should use proxy
                    bool useProxy = _configuration["Discord:UseProxy"]?.ToLower() == "true";
                    if (useProxy)
                    {
                        _logger.LogInformation("Using system proxy for Discord connection");

                        // Set environment variable to use system proxy
                        Environment.SetEnvironmentVariable("HTTP_PROXY", "");
                        Environment.SetEnvironmentVariable("HTTPS_PROXY", "");

                        // This will make .NET HttpClient use system proxy
                        System.Net.WebRequest.DefaultWebProxy = System.Net.WebRequest.GetSystemWebProxy();
                        System.Net.WebRequest.DefaultWebProxy.Credentials = System.Net.CredentialCache.DefaultCredentials;
                    }
                    else
                    {
                        _logger.LogInformation("Not using system proxy for Discord connection");
                    }

                    // Create Discord client with all required intents
                    var config = new DiscordSocketConfig
                    {
                        GatewayIntents = GatewayIntents.MessageContent |
                                        GatewayIntents.GuildMessages |
                                        GatewayIntents.Guilds |
                                        GatewayIntents.DirectMessages |
                                        GatewayIntents.GuildMembers |  // Add GuildMembers intent
                                        GatewayIntents.GuildPresences, // Add GuildPresences intent for completeness
                        AlwaysDownloadUsers = true,
                        LogLevel = LogSeverity.Debug, // Set to Debug for more detailed logs
                        UseSystemClock = true
                    };

                    _discordClient = new DiscordSocketClient(config);

                    _logger.LogInformation("Discord client created with intents: {Intents}",
                        GatewayIntents.MessageContent | GatewayIntents.GuildMessages | GatewayIntents.Guilds |
                        GatewayIntents.DirectMessages | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences);

                    // Set up event handlers
                    _discordClient.Log += LogAsync;
                    _discordClient.MessageReceived += DiscordMessageReceivedAsync;
                    _discordClient.Ready += ReadyAsync;
                    _discordClient.Disconnected += DisconnectedAsync;

                    _logger.LogInformation("Discord event handlers registered");

                    // Log in and start
                    _logger.LogInformation("Attempting to log in to Discord...");
                    await _discordClient.LoginAsync(TokenType.Bot, _botToken);

                    _logger.LogInformation("Login successful, starting Discord client...");
                    await _discordClient.StartAsync();

                    _logger.LogInformation("Discord bot client initialized and started");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Discord bot client: {Message}", ex.Message);
                    if (ex.InnerException != null)
                    {
                        _logger.LogError("Inner exception: {Message}", ex.InnerException.Message);
                    }
                    _discordClient = null;
                }
            }
            else
            {
                _logger.LogInformation("Discord bot client not initialized (no bot token provided)");
            }

            // Check if we need to automatically manage webhooks
            if (_autoWebhook && !string.IsNullOrEmpty(_botToken) && _channelId != 0)
            {
                try
                {
                    _logger.LogInformation("Attempting to manage Discord webhook...");
                    var webhookUrl = await ManageWebhookAsync(_channelId, _webhookName);

                    if (!string.IsNullOrEmpty(webhookUrl))
                    {
                        _webhookUrl = webhookUrl;
                        _logger.LogInformation("Discord webhook managed successfully: {WebhookUrl}",
                            webhookUrl.Substring(0, Math.Min(20, webhookUrl.Length)) + "...");
                    }
                    else
                    {
                        _logger.LogWarning("Failed to manage Discord webhook");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error managing Discord webhook: {Message}", ex.Message);
                }
            }

            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                _logger.LogInformation("Discord webhook initialized");
            }
            else
            {
                _logger.LogInformation("Discord webhook not initialized (no webhook URL provided)");
            }
        }

        private Task LogAsync(LogMessage log)
        {
            // Log the message with appropriate log level
            switch (log.Severity)
            {
                case LogSeverity.Critical:
                    _logger.LogCritical("Discord: {Message}", log.Message);
                    break;
                case LogSeverity.Error:
                    _logger.LogError("Discord: {Message}", log.Message);
                    break;
                case LogSeverity.Warning:
                    _logger.LogWarning("Discord: {Message}", log.Message);
                    break;
                case LogSeverity.Info:
                    _logger.LogInformation("Discord: {Message}", log.Message);
                    break;
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                default:
                    _logger.LogDebug("Discord: {Message}", log.Message);
                    break;
            }

            // Log exception details if available
            if (log.Exception != null)
            {
                _logger.LogError(log.Exception, "Discord error: {Source} - {Message}",
                    log.Source, log.Exception.Message);

                // Log inner exception if available
                if (log.Exception.InnerException != null)
                {
                    _logger.LogError("Discord inner error: {Message}",
                        log.Exception.InnerException.Message);
                }
            }

            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            _logger.LogInformation("Discord bot is connected and ready");
            return Task.CompletedTask;
        }

        private Task DisconnectedAsync(Exception ex)
        {
            _logger.LogWarning("Discord bot disconnected: {Message}", ex?.Message ?? "Unknown reason");
            if (ex != null)
            {
                _logger.LogError(ex, "Disconnect exception details");
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: {Message}", ex.InnerException.Message);
                }
            }
            return Task.CompletedTask;
        }

        private Task DiscordMessageReceivedAsync(SocketMessage message)
        {
            // Ignore system messages and messages from bots
            if (message is not SocketUserMessage userMessage || userMessage.Author.IsBot)
                return Task.CompletedTask;

            // Check if this is a message from the configured channel
            if (userMessage.Channel is SocketGuildChannel guildChannel)
            {
                // If guild ID and channel ID are configured, check if they match
                if (_guildId != 0 && _channelId != 0)
                {
                    if (guildChannel.Guild.Id != _guildId || guildChannel.Id != _channelId)
                        return Task.CompletedTask;
                }
                // If only guild ID is configured, check if it matches
                else if (_guildId != 0)
                {
                    if (guildChannel.Guild.Id != _guildId)
                        return Task.CompletedTask;
                }
                // If only channel ID is configured, check if it matches
                else if (_channelId != 0)
                {
                    if (guildChannel.Id != _channelId)
                        return Task.CompletedTask;
                }
            }
            // If we're in a DM channel and channel ID is configured, check if it matches
            else if (userMessage.Channel is SocketDMChannel dmChannel && _channelId != 0)
            {
                if (dmChannel.Id != _channelId)
                    return Task.CompletedTask;
            }

            // Create a message object
            // Get user avatar URL
            string avatarUrl = userMessage.Author.GetAvatarUrl() ?? userMessage.Author.GetDefaultAvatarUrl();

            // Log avatar URL status
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                _logger.LogInformation("Successfully retrieved Discord avatar URL for user {UserId}: {AvatarUrl}",
                    userMessage.Author.Id, avatarUrl);
            }
            else
            {
                _logger.LogWarning("Failed to get Discord avatar for user {UserId}. Using default avatar.",
                    userMessage.Author.Id);
                avatarUrl = "https://avatars.githubusercontent.com/u/197464182";
            }

            var crossPlatformMessage = new Message
            {
                Id = userMessage.Id.ToString(),
                Content = userMessage.Content,
                SenderName = userMessage.Author.Username,
                SenderId = userMessage.Author.Id.ToString(),
                Source = MessageSource.Discord,
                Timestamp = userMessage.Timestamp.DateTime,
                AvatarUrl = avatarUrl
            };

            // If there's an attachment, add it as an image URL
            if (userMessage.Attachments.Count > 0)
            {
                var attachment = userMessage.Attachments.First();
                if (attachment.ContentType?.StartsWith("image/") == true)
                {
                    crossPlatformMessage.ImageUrl = attachment.Url;
                }
            }

            _logger.LogDebug("Received message from Discord: {Message}", crossPlatformMessage.Content);

            // Invoke the event
            MessageReceived?.Invoke(this, crossPlatformMessage);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // Clean up resources
            _cts?.Cancel();
            _cts?.Dispose();

            // Dispose Discord client
            if (_discordClient != null)
            {
                _discordClient.LogoutAsync().GetAwaiter().GetResult();
                _discordClient.Dispose();
            }

            // Suppress finalization
            GC.SuppressFinalize(this);
        }

        public async Task SendMessageAsync(Message message)
        {
            try
            {
                // Format the message with source and sender info
                string content = message.ToString();

                // Try to send via webhook if available
                if (!string.IsNullOrEmpty(_webhookUrl))
                {
                    // Get avatar URL
                    string avatarUrl = message.AvatarUrl ?? "https://avatars.githubusercontent.com/u/197464182";

                    // Log avatar URL status
                    if (!string.IsNullOrEmpty(message.AvatarUrl))
                    {
                        _logger.LogInformation("Using sender's avatar URL for Discord webhook: {AvatarUrl}", message.AvatarUrl);
                    }
                    else
                    {
                        _logger.LogWarning("No avatar URL provided for sender. Using default avatar.");
                    }

                    // Create the webhook payload with sender's formatted name and avatar
                    // For webhook, we use the original content without special formatting
                    var payload = new
                    {
                        content = message.Content, // Use original content without special formatting
                        username = message.GetFormattedUsername(), // Use the formatted username: <user>@<platform>
                        avatar_url = avatarUrl // Use sender's avatar or default
                    };

                    // If there's an image URL, we could add it as an embed
                    if (!string.IsNullOrEmpty(message.ImageUrl))
                    {
                        var payloadWithEmbed = new
                        {
                            content = message.Content, // Use original content without special formatting
                            username = message.GetFormattedUsername(), // Use the formatted username: <user>@<platform>
                            avatar_url = avatarUrl, // Use the same avatar URL as in the regular payload
                            embeds = new[]
                            {
                                new
                                {
                                    image = new
                                    {
                                        url = message.ImageUrl
                                    }
                                }
                            }
                        };

                        await _httpClient.PostJsonAsync(_webhookUrl, payloadWithEmbed);
                    }
                    else
                    {
                        await _httpClient.PostJsonAsync(_webhookUrl, payload);
                    }
                    _logger.LogInformation("Message sent to Discord via webhook");
                }
                // Try to send via bot if available
                else if (_discordClient != null && _discordClient.ConnectionState == ConnectionState.Connected)
                {
                    // Try to get the channel
                    if (_channelId != 0)
                    {
                        var channel = _discordClient.GetChannel(_channelId) as IMessageChannel;
                        if (channel != null)
                        {
                            // Format the message according to the requested format: **<user>@<platform>**\n<msg>
                            string formattedUsername = message.GetFormattedUsername();
                            string formattedContent = $"**{formattedUsername}**\n{message.Content}";

                            // Send the message
                            if (!string.IsNullOrEmpty(message.ImageUrl))
                            {
                                // Create an embed with the image
                                var embed = new EmbedBuilder()
                                    .WithImageUrl(message.ImageUrl)
                                    .Build();

                                await channel.SendMessageAsync(text: formattedContent, embed: embed);
                            }
                            else
                            {
                                await channel.SendMessageAsync(text: formattedContent);
                            }

                            _logger.LogInformation("Message sent to Discord via bot with formatted username");
                        }
                        else
                        {
                            _logger.LogWarning("Failed to find Discord channel with ID {ChannelId}", _channelId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot send message via bot: No channel ID configured");
                    }
                }
                else
                {
                    _logger.LogWarning("Cannot send message to Discord: No webhook URL or connected bot available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to Discord");
            }
        }

        public Task StartListeningAsync()
        {
            // Discord bot is already listening if it's connected
            if (_discordClient != null && _discordClient.ConnectionState == ConnectionState.Connected)
            {
                _logger.LogInformation("Discord bot is already listening for messages");
            }
            else if (_discordClient != null)
            {
                _logger.LogWarning("Discord bot is not connected, cannot listen for messages");
            }
            else if (!string.IsNullOrEmpty(_webhookUrl))
            {
                _logger.LogInformation("Discord adapter started listening (webhook only, no incoming messages)");
            }
            else
            {
                _logger.LogWarning("Discord adapter has no webhook URL or bot token, cannot listen for messages");
            }

            return Task.CompletedTask;
        }

        public Task StopListeningAsync()
        {
            // Nothing to do for webhook
            if (_discordClient != null)
            {
                try
                {
                    // We don't actually stop listening, as that would disconnect the bot
                    // Just log that we're still connected
                    _logger.LogInformation("Discord bot remains connected (to stop completely, dispose the adapter)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping Discord adapter");
                }
            }
            else
            {
                _logger.LogInformation("Discord adapter stopped listening");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Manages webhooks - creates, updates, or reuses existing webhooks
        /// </summary>
        /// <param name="channelId">The Discord channel ID</param>
        /// <param name="name">The name for the webhook</param>
        /// <returns>The webhook URL if successful, null otherwise</returns>
        private async Task<string?> ManageWebhookAsync(ulong channelId, string name)
        {
            if (_discordClient == null || _discordClient.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogWarning("Cannot manage webhook: Discord client is not connected");
                return null;
            }

            try
            {
                // Get the target channel
                var targetChannel = _discordClient.GetChannel(channelId);

                if (targetChannel is not ITextChannel targetTextChannel)
                {
                    _logger.LogWarning("Cannot manage webhook: Channel {ChannelId} is not a text channel", channelId);
                    return null;
                }

                // First, check if a webhook with this name already exists in any channel
                bool foundInWrongChannel = false;
                IWebhook? existingWebhook = null;
                ITextChannel? existingChannel = null;

                // Get all guild channels
                var guild = _discordClient.GetGuild(_guildId);
                if (guild == null)
                {
                    _logger.LogWarning("Cannot manage webhook: Guild {GuildId} not found", _guildId);
                    return null;
                }

                // Check each text channel for webhooks with our name
                foreach (var channel in guild.TextChannels)
                {
                    var webhooks = await channel.GetWebhooksAsync();
                    var webhook = webhooks.FirstOrDefault(w => w.Name == name);

                    if (webhook != null)
                    {
                        existingWebhook = webhook;
                        existingChannel = channel;

                        // If the webhook is in the wrong channel, we'll need to move it
                        if (channel.Id != channelId)
                        {
                            foundInWrongChannel = true;
                            _logger.LogInformation("Found webhook '{Name}' in channel {ChannelName}, but it needs to be in {TargetChannelName}",
                                name, channel.Name, targetTextChannel.Name);
                        }
                        else
                        {
                            _logger.LogInformation("Webhook with name '{Name}' already exists in the correct channel, using existing webhook", name);
                            return $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}";
                        }

                        break;
                    }
                }

                // If we found the webhook in the wrong channel, try to modify it
                if (foundInWrongChannel && existingWebhook != null)
                {
                    try
                    {
                        // Try to modify the webhook to point to the new channel
                        // Note: This requires the MANAGE_WEBHOOKS permission
                        await existingWebhook.ModifyAsync(props =>
                        {
                            props.ChannelId = channelId;
                        }, new RequestOptions
                        {
                            AuditLogReason = "Updated by Cross-Platform Messenger to point to the correct channel"
                        });

                        _logger.LogInformation("Successfully moved webhook '{Name}' from channel {OldChannelName} to {NewChannelName}",
                            name, existingChannel?.Name ?? "unknown", targetTextChannel.Name);

                        return $"https://discord.com/api/webhooks/{existingWebhook.Id}/{existingWebhook.Token}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to move webhook to the correct channel. Will try to create a new one.");
                        // Continue to create a new webhook
                    }
                }

                // If we didn't find a webhook or couldn't move it, create a new one
                var newWebhook = await targetTextChannel.CreateWebhookAsync(name, options: new RequestOptions
                {
                    AuditLogReason = "Created by Cross-Platform Messenger"
                });

                _logger.LogInformation("Created new webhook with name '{Name}' in channel {ChannelName}",
                    name, targetTextChannel.Name);

                // Construct the webhook URL
                return $"https://discord.com/api/webhooks/{newWebhook.Id}/{newWebhook.Token}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing webhook: {Message}", ex.Message);
                return null;
            }
        }
    }
}
