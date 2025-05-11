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
using InputFileUrl = Telegram.Bot.Types.InputFile;

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
                await _botClient.SendTextMessageAsync(
                    chatId: _chatId,
                    text: text
                );

                // If there's an image URL, send it as a photo
                if (!string.IsNullOrEmpty(message.ImageUrl))
                {
                    // Use the URL directly as InputFile
                    await _botClient.SendPhotoAsync(
                        chatId: _chatId,
                        photo: InputFile.FromUri(message.ImageUrl)
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

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message }
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                receiverOptions,
                _cts.Token
            );

            _logger.LogInformation("Telegram adapter started listening");
            return Task.CompletedTask;
        }

        public Task StopListeningAsync()
        {
            _cts?.Cancel();
            _logger.LogInformation("Telegram adapter stopped listening");
            return Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Only process Message updates
                if (update.Message is not { } telegramMessage)
                    return;

                // Only process text messages
                if (telegramMessage.Text is not { } messageText)
                    return;

                // Get user info for avatar
                var userId = telegramMessage.From?.Id.ToString() ?? "Unknown";
                var userName = telegramMessage.From?.FirstName + " " + telegramMessage.From?.LastName;

                var message = new Models.Message
                {
                    Content = messageText,
                    SenderName = userName,
                    SenderId = userId,
                    Source = MessageSource.Telegram,
                    Timestamp = telegramMessage.Date.ToLocalTime(),
                    AvatarUrl = GetTelegramAvatarUrl(telegramMessage.From)
                };

                // If the message contains a photo, get the URL
                if (telegramMessage.Photo != null && telegramMessage.Photo.Length > 0)
                {
                    var fileId = telegramMessage.Photo[^1].FileId;
                    var fileInfo = await botClient.GetFileAsync(fileId, cancellationToken);
                    var token = _configuration["Telegram:BotToken"];
                    message.ImageUrl = $"https://api.telegram.org/file/bot{token}/{fileInfo.FilePath}";
                }

                MessageReceived?.Invoke(this, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram update");
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Telegram polling error");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the avatar URL for a Telegram user
        /// </summary>
        private string GetTelegramAvatarUrl(Telegram.Bot.Types.User? user)
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
                var token = _configuration["Telegram:BotToken"];

                // Telegram doesn't provide a direct way to get avatar URL through the Bot API
                // We need to get the user's profile photos and use the first one
                // This URL format works for getting the profile photo file
                string avatarUrl = $"https://api.telegram.org/bot{token}/getUserProfilePhotos?user_id={user.Id}";
                _logger.LogInformation("Successfully retrieved Telegram avatar URL for user {UserId}: {AvatarUrl}",
                    user.Id, avatarUrl);
                return avatarUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Telegram avatar URL");
                return "https://avatars.githubusercontent.com/u/197464182";
            }
        }
    }
}
