using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using InputFile = Telegram.Bot.Types.InputFile;

// Use alias to avoid ambiguity with our own Message class
using TelegramMessage = Telegram.Bot.Types.Message;

namespace DC.QQ.TG.Adapters
{
    public class TelegramAdapter : IMessageAdapter
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramAdapter> _logger;
        private TelegramBotClient _botClient;
        private CancellationTokenSource _cts;
        private string _chatId;

        // Webhook related fields
        private string _webhookUrl;
        private readonly string _webhookPath = "/telegram-webhook";
        private int _webhookPort = 8443; // Default Telegram webhook port
        private HttpListener _httpListener;
        private bool _useWebhook;

        public MessageSource Platform => MessageSource.Telegram;

        public event EventHandler<Models.Message> MessageReceived;

        public TelegramAdapter(IConfiguration configuration, ILogger<TelegramAdapter> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task InitializeAsync()
        {
            // Trying to fix telegram inbound message issue
            _logger.LogInformation("Initializing Telegram adapter...");

            var botToken = _configuration["Telegram:BotToken"];
            _chatId = _configuration["Telegram:ChatId"];
            _webhookUrl = _configuration["Telegram:WebhookUrl"];
            _useWebhook = !string.IsNullOrEmpty(_webhookUrl);

            // Get webhook port from configuration or use default
            if (int.TryParse(_configuration["Telegram:WebhookPort"], out int port))
            {
                _webhookPort = port;
                _logger.LogInformation("Using custom webhook port: {Port}", _webhookPort);
            }

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(_chatId))
            {
                throw new InvalidOperationException("Telegram configuration is missing or invalid");
            }

            _logger.LogInformation("Creating Telegram bot client with token: {BotToken}...", botToken[..10] + "......");
            _botClient = new TelegramBotClient(botToken);
            _logger.LogInformation("Telegram bot client created successfully");

            // Log webhook status
            if (_useWebhook)
            {
                _logger.LogInformation("Telegram webhook will be used: {WebhookUrl}", _webhookUrl);
            }
            else
            {
                _logger.LogInformation("Telegram webhook is not configured, using event-based approach");
            }

            _logger.LogInformation("Telegram adapter initialized with chat ID: {ChatId}", _chatId);
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(Models.Message message)
        {
            try
            {
                // Format the message with source and sender info using HTML
                string username = message.GetFormattedUsername();
                string content = message.Content;

                // Format the message with HTML
                string text = $"<b>{username}</b>:\n{content}";

                // Send text message with HTML formatting
                await _botClient.SendMessage(
                    chatId: _chatId,
                    text: text,
                    parseMode: ParseMode.Html
                );

                // If there's an image URL, send it as a photo
                if (!string.IsNullOrEmpty(message.ImageUrl))
                {
                    // Format caption with HTML if needed
                    string caption = null;

                    // If we want to add a caption to the image, we can do it here
                    // caption = $"<b>{message.GetFormattedUsername()}</b>";

                    // Send the photo using the URL directly
                    await _botClient.SendPhoto(
                        chatId: _chatId,
                        photo: InputFile.FromUri(new Uri(message.ImageUrl)),
                        caption: caption,
                        parseMode: caption != null ? ParseMode.Html : default
                    );
                }

                _logger.LogInformation("Message sent to Telegram");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to Telegram");
            }
        }

        public async Task StartListeningAsync()
        {
            _cts = new CancellationTokenSource();

            _logger.LogInformation("Starting Telegram receiver with chat ID: {ChatId}", _chatId);

            try
            {
                if (_useWebhook && !string.IsNullOrEmpty(_webhookUrl))
                {
                    // Set up webhook
                    _logger.LogInformation("Setting up Telegram webhook at {WebhookUrl}", _webhookUrl);

                    // Set the webhook
                    await _botClient.SetWebhook(_webhookUrl);

                    // Start the webhook listener
                    await StartWebhookListenerAsync();

                    _logger.LogInformation("Telegram adapter started listening successfully using webhook");
                }
                else
                {
                    // Delete webhook to ensure we're using long polling
                    _logger.LogInformation("Deleting any existing webhook to use event-based approach");
                    await _botClient.DeleteWebhook();

                    // Drop pending updates
                    await _botClient.DropPendingUpdates();

                    // Subscribe to update events
                    _botClient.OnUpdate += OnUpdateReceived; // Add support for all update types
                    _botClient.OnError += OnErrorReceived;

                    _logger.LogInformation("Telegram adapter started listening successfully using event-based approach");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Telegram receiver");
                throw;
            }
        }

        private Task StartWebhookListenerAsync()
        {
            try
            {
                // Create HTTP listener
                _httpListener = new HttpListener();

                // Extract hostname from webhook URL
                var uri = new Uri(_webhookUrl);

                // Use the configured port instead of the one in the URL if it's different
                int port = uri.Port;
                if (port != _webhookPort && _webhookPort != 0)
                {
                    _logger.LogInformation("Using custom port {CustomPort} instead of URL port {UrlPort}", _webhookPort, uri.Port);
                    port = _webhookPort;
                }

                string prefix = $"{uri.Scheme}://{uri.Host}:{port}{_webhookPath}/";

                _logger.LogInformation("Starting HTTP listener on {Prefix}", prefix);
                _httpListener.Prefixes.Add(prefix);
                _httpListener.Start();

                // Start listening for requests in a background task
                _ = Task.Run(WebhookListenerLoopAsync);

                _logger.LogInformation("Webhook listener started successfully");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start webhook listener");
                throw;
            }
        }

        private async Task WebhookListenerLoopAsync()
        {
            try
            {
                while (_httpListener.IsListening && !_cts.Token.IsCancellationRequested)
                {
                    // Wait for a request
                    var context = await _httpListener.GetContextAsync();

                    // Process the request in a separate task
                    _ = Task.Run(async () => await ProcessWebhookRequestAsync(context));
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, no need to log
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook listener loop");
            }
            finally
            {
                _logger.LogInformation("Webhook listener loop exited");
            }
        }

        private async Task ProcessWebhookRequestAsync(HttpListenerContext context)
        {
            try
            {
                // Read the request body
                using var reader = new StreamReader(context.Request.InputStream);
                string json = await reader.ReadToEndAsync();

                _logger.LogDebug("Received webhook request: {Json}", json);

                // Parse the update
                var update = JsonConvert.DeserializeObject<Update>(json);

                if (update != null)
                {
                    // Process the update
                    await OnUpdateReceived(update);

                    // Send a 200 OK response
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
                else
                {
                    _logger.LogWarning("Failed to parse webhook request as Update object");
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook request");
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }

        public async Task StopListeningAsync()
        {
            _logger.LogInformation("Stopping Telegram adapter...");

            // Cancel the token source
            _cts?.Cancel();

            if (_useWebhook)
            {
                try
                {
                    // Delete the webhook
                    _logger.LogInformation("Deleting Telegram webhook");
                    await _botClient.DeleteWebhook();

                    // Stop the HTTP listener
                    if (_httpListener != null && _httpListener.IsListening)
                    {
                        _logger.LogInformation("Stopping HTTP listener");
                        _httpListener.Stop();
                        _httpListener.Close();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping webhook listener");
                }
            }
            else
            {
                // Unsubscribe from events
                _botClient.OnUpdate -= OnUpdateReceived;
                _botClient.OnError -= OnErrorReceived;
            }

            _logger.LogInformation("Telegram adapter stopped listening");
        }

        /// <summary>
        /// Gets Telegram chat info and bot status for debugging purposes
        /// </summary>
        /// <returns>A string containing the chat info and bot status</returns>
        public async Task<string> GetRecentMessagesAsync()
        {
            try
            {
                if (_botClient == null)
                {
                    return "Telegram bot client is not initialized.";
                }

                _logger.LogInformation("Retrieving Telegram chat info and bot status for chat {ChatId}", _chatId);

                // Create a new cancellation token source for this operation
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 second timeout

                var result = new StringBuilder();
                result.AppendLine("=== Telegram Debug Information ===");
                result.AppendLine();

                // Get bot info
                User me = null;
                try
                {
                    me = await _botClient.GetMe(cts.Token);
                    result.AppendLine("Bot Information:");
                    result.AppendLine($"- ID: {me.Id}");
                    result.AppendLine($"- Name: {me.FirstName}");
                    result.AppendLine($"- Username: @{me.Username}");
                    result.AppendLine($"- Can join groups: {me.CanJoinGroups}");
                    result.AppendLine($"- Can read all group messages: {me.CanReadAllGroupMessages}");
                    result.AppendLine($"- Supports inline queries: {me.SupportsInlineQueries}");
                    result.AppendLine();
                }
                catch (Exception ex)
                {
                    result.AppendLine($"Error retrieving bot info: {ex.Message}");
                    result.AppendLine();
                }

                // Get chat info
                try
                {
                    var chatId = long.Parse(_chatId);
                    var chat = await _botClient.GetChat(chatId, cts.Token);

                    result.AppendLine("Chat Information:");
                    result.AppendLine($"- ID: {chat.Id}");
                    result.AppendLine($"- Type: {chat.Type}");
                    result.AppendLine($"- Title: {chat.Title ?? "N/A"}");
                    result.AppendLine($"- Username: {chat.Username ?? "N/A"}");
                    result.AppendLine($"- Description: {chat.Description ?? "N/A"}");
                    result.AppendLine($"- Invite Link: {chat.InviteLink ?? "N/A"}");
                    result.AppendLine();

                    // Try to get chat member count
                    try
                    {
                        var memberCount = await _botClient.GetChatMemberCount(chatId, cts.Token);
                        result.AppendLine($"- Member Count: {memberCount}");
                    }
                    catch
                    {
                        result.AppendLine("- Member Count: Unable to retrieve");
                    }

                    // Try to get bot's member info
                    try
                    {
                        if (me != null)
                        {
                            var botMember = await _botClient.GetChatMember(chatId, me.Id, cts.Token);
                            result.AppendLine($"- Bot's Status in Chat: {botMember.Status}");
                        }
                        else
                        {
                            result.AppendLine("- Bot's Status in Chat: Unable to retrieve (bot info not available)");
                        }
                    }
                    catch
                    {
                        result.AppendLine("- Bot's Status in Chat: Unable to retrieve");
                    }

                    result.AppendLine();
                }
                catch (Exception ex)
                {
                    result.AppendLine($"Error retrieving chat info: {ex.Message}");
                    result.AppendLine();
                }

                // Send a test message to verify sending capabilities
                try
                {
                    var sentMessage = await _botClient.SendMessage(
                        chatId: _chatId,
                        text: "This is a test message to verify bot functionality.",
                        cancellationToken: cts.Token
                    );

                    result.AppendLine("Test Message:");
                    result.AppendLine($"- Successfully sent test message with ID: {sentMessage.MessageId}");
                    result.AppendLine($"- Sent at: {sentMessage.Date.ToLocalTime()}");
                    result.AppendLine();
                }
                catch (Exception ex)
                {
                    result.AppendLine($"Error sending test message: {ex.Message}");
                    result.AppendLine();
                }

                // Note about message history limitation
                result.AppendLine("Note: Telegram Bot API does not provide a method to retrieve chat history.");
                result.AppendLine("The bot can only process messages it receives while running.");
                result.AppendLine("To see message history, please check the Telegram chat directly.");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Telegram debug information");
                return $"Error retrieving Telegram debug information: {ex.Message}";
            }
        }

        private Task OnErrorReceived(Exception exception, HandleErrorSource source)
        {
            _logger.LogError(exception, "Telegram error from source {Source}", source);
            return Task.CompletedTask;
        }

        private async Task OnUpdateReceived(Update update)
        {
            try
            {
                _logger.LogDebug("Received update type: {UpdateType}", update.Type);

                // Handle different update types
                switch (update.Type)
                {
                    case UpdateType.Message:
                        if (update.Message != null)
                            await ProcessMessageAsync(update.Message);
                        break;
                    case UpdateType.ChannelPost:
                        if (update.ChannelPost != null)
                            await ProcessMessageAsync(update.ChannelPost);
                        break;
                    case UpdateType.EditedMessage:
                        if (update.EditedMessage != null)
                            await ProcessMessageAsync(update.EditedMessage);
                        break;
                    case UpdateType.EditedChannelPost:
                        if (update.EditedChannelPost != null)
                            await ProcessMessageAsync(update.EditedChannelPost);
                        break;
                    default:
                        _logger.LogDebug("Unhandled update type: {UpdateType}", update.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Telegram update");
            }
        }

        private async Task ProcessMessageAsync(TelegramMessage telegramMessage)
        {
            // Check if the message is from the configured chat
            if (telegramMessage.Chat.Id.ToString() != _chatId)
            {
                _logger.LogDebug("Ignoring message from chat {ChatId} (configured chat is {ConfiguredChatId})",
                    telegramMessage.Chat.Id, _chatId);
                return;
            }

            // Get user info
            var userId = telegramMessage.From?.Id.ToString() ?? "Unknown";
            var firstName = telegramMessage.From?.FirstName ?? "";
            var lastName = telegramMessage.From?.LastName ?? "";
            var userName = string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}";

            // Use username if available
            if (!string.IsNullOrEmpty(telegramMessage.From?.Username))
            {
                userName = telegramMessage.From.Username;
            }

            // Extract message content
            string messageText;

            // Process text messages
            if (!string.IsNullOrEmpty(telegramMessage.Text))
            {
                messageText = telegramMessage.Text;
            }
            // Process caption from media messages
            else if (!string.IsNullOrEmpty(telegramMessage.Caption))
            {
                messageText = telegramMessage.Caption;
            }
            // Handle other message types
            else if (telegramMessage.Sticker != null)
            {
                messageText = $"[Sticker: {telegramMessage.Sticker.Emoji}]";
            }
            else if (telegramMessage.Animation != null)
            {
                messageText = "[GIF]";
            }
            else if (telegramMessage.Video != null)
            {
                messageText = "[Video]";
            }
            else if (telegramMessage.Audio != null)
            {
                messageText = "[Audio]";
            }
            else if (telegramMessage.Voice != null)
            {
                messageText = "[Voice Message]";
            }
            else if (telegramMessage.Document != null)
            {
                messageText = $"[Document: {telegramMessage.Document.FileName}]";
            }
            else if (telegramMessage.Location != null)
            {
                messageText = $"[Location: {telegramMessage.Location.Latitude}, {telegramMessage.Location.Longitude}]";
            }
            else if (telegramMessage.Poll != null)
            {
                messageText = $"[Poll: {telegramMessage.Poll.Question}]";
            }
            else if (telegramMessage.Photo != null && telegramMessage.Photo.Length > 0)
            {
                messageText = "[Photo]";
            }
            else
            {
                messageText = "[Unsupported message type]";
            }

            // Create message object
            var message = new Models.Message
            {
                Content = messageText,
                SenderName = userName,
                SenderId = userId,
                Source = MessageSource.Telegram,
                Timestamp = telegramMessage.Date.ToLocalTime(),
                AvatarUrl = await GetTelegramAvatarUrlAsync(telegramMessage.From, _botClient, _cts.Token)
            };

            // If the message contains a photo, get the URL
            if (telegramMessage.Photo != null && telegramMessage.Photo.Length > 0)
            {
                var fileId = telegramMessage.Photo[^1].FileId;
                var fileInfo = await _botClient.GetFile(fileId, _cts.Token);
                var token = _configuration["Telegram:BotToken"];
                message.ImageUrl = $"https://api.telegram.org/file/bot{token}/{fileInfo.FilePath}";
            }
            // If the message contains a sticker, get the URL
            else if (telegramMessage.Sticker != null && telegramMessage.Sticker.IsAnimated == false)
            {
                var fileId = telegramMessage.Sticker.FileId;
                var fileInfo = await _botClient.GetFile(fileId, _cts.Token);
                var token = _configuration["Telegram:BotToken"];
                message.ImageUrl = $"https://api.telegram.org/file/bot{token}/{fileInfo.FilePath}";
            }

            _logger.LogInformation("Received message from Telegram: {Message}", messageText);
            MessageReceived?.Invoke(this, message);
        }

        /// <summary>
        /// Gets the avatar URL for a Telegram user
        /// </summary>
        private async Task<string> GetTelegramAvatarUrlAsync(User user, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            // If user is null or we don't have a bot token, return default avatar
            if (user == null || string.IsNullOrEmpty(_configuration["Telegram:BotToken"]))
            {
                _logger.LogWarning("Failed to get Telegram avatar: User is null or bot token is missing. Using default avatar.");
                return "https://avatars.githubusercontent.com/u/197464182";
            }

            try
            {
                // Try to get user profile photos
                var photos = await botClient.GetUserProfilePhotos(user.Id, 0, 1, cancellationToken);

                // Check if user has profile photos
                if (photos.TotalCount > 0 && photos.Photos.Length > 0 && photos.Photos[0].Length > 0)
                {
                    // Get the file path for the photo
                    var fileId = photos.Photos[0][^1].FileId; // Get the highest resolution photo
                    var fileInfo = await botClient.GetFile(fileId, cancellationToken);
                    var token = _configuration["Telegram:BotToken"];

                    // Construct the URL to the photo
                    string avatarUrl = $"https://api.telegram.org/file/bot{token}/{fileInfo.FilePath}";
                    _logger.LogInformation("Successfully retrieved Telegram avatar URL for user {UserId}", user.Id);
                    return avatarUrl;
                }
                else
                {
                    _logger.LogWarning("User {UserId} has no profile photos. Using default avatar.", user.Id);
                    return "https://avatars.githubusercontent.com/u/197464182";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Telegram avatar URL for user {UserId}", user.Id);
                return "https://avatars.githubusercontent.com/u/197464182";
            }
        }
    }
}
