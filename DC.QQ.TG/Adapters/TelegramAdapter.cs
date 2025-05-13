using System;
using System.Threading;
using System.Threading.Tasks;
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

        public MessageSource Platform => MessageSource.Telegram;

        public event EventHandler<Models.Message> MessageReceived;

        public TelegramAdapter(IConfiguration configuration, ILogger<TelegramAdapter> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task InitializeAsync()
        {
            var botToken = _configuration["Telegram:BotToken"];
            _chatId = _configuration["Telegram:ChatId"];

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(_chatId))
            {
                throw new InvalidOperationException("Telegram configuration is missing or invalid");
            }

            _botClient = new TelegramBotClient(botToken);
            _logger.LogInformation("Telegram adapter initialized");

            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(Models.Message message)
        {
            try
            {
                // Format the message with source and sender info
                string text = $"{message.GetFormattedUsername()}: {message.Content}";

                // Send text message
                await _botClient.SendMessage(
                    chatId: _chatId,
                    text: text
                );

                // If there's an image URL, send it as a photo
                if (!string.IsNullOrEmpty(message.ImageUrl))
                {
                    // Send the photo using the URL directly
                    await _botClient.SendPhoto(
                        chatId: _chatId,
                        photo: InputFile.FromUri(new Uri(message.ImageUrl))
                    );
                }

                _logger.LogInformation("Message sent to Telegram");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to Telegram");
            }
        }

        public Task StartListeningAsync()
        {
            _cts = new CancellationTokenSource();

            _logger.LogInformation("Starting Telegram receiver with chat ID: {ChatId}", _chatId);

            try
            {
                // Subscribe to update events
                _botClient.OnMessage += OnMessageReceived;

                _logger.LogInformation("Telegram adapter started listening successfully using event-based approach");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Telegram receiver");
                throw;
            }

            return Task.CompletedTask;
        }

        public Task StopListeningAsync()
        {
            // Unsubscribe from events
            _botClient.OnMessage -= OnMessageReceived;

            // Cancel the token source
            _cts?.Cancel();

            _logger.LogInformation("Telegram adapter stopped listening");
            return Task.CompletedTask;
        }



        private async Task OnMessageReceived(TelegramMessage telegramMessage, UpdateType updateType)
        {
            try
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
                string messageText = "";

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram message");
            }
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
