using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Models;
using DC.QQ.TG.Utils;
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
        private string _webhookPath = ""; // Default to empty path, will be extracted from URL
        private int _webhookPort = 8443; // Default Telegram webhook port
        private string _certificatePath; // Path to SSL certificate file
        private string _certificatePassword; // Password for SSL certificate
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

            // Validate webhook URL if provided
            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                if (!Uri.TryCreate(_webhookUrl, UriKind.Absolute, out Uri uri))
                {
                    _logger.LogWarning("Invalid webhook URL: {WebhookUrl}. Webhook will not be used.", _webhookUrl);
                    _useWebhook = false;
                }
                else if (uri.Scheme != "https")
                {
                    _logger.LogWarning("Webhook URL must use HTTPS: {WebhookUrl}. Webhook will not be used.", _webhookUrl);
                    _useWebhook = false;
                }
                else
                {
                    _useWebhook = true;
                    _logger.LogInformation("Using webhook for Telegram: {WebhookUrl}", _webhookUrl);
                }
            }
            else
            {
                _useWebhook = false;
            }

            // Get webhook port from configuration or use default
            if (int.TryParse(_configuration["Telegram:WebhookPort"], out int port))
            {
                _webhookPort = port;
                _logger.LogInformation("Using custom webhook port: {Port}", _webhookPort);
            }

            // Get certificate path and password from configuration
            _certificatePath = _configuration["Telegram:CertificatePath"];
            _certificatePassword = _configuration["Telegram:CertificatePassword"];

            if (!string.IsNullOrEmpty(_certificatePath))
            {
                if (File.Exists(_certificatePath))
                {
                    _logger.LogInformation("Using SSL certificate from: {CertificatePath}", _certificatePath);
                }
                else
                {
                    _logger.LogWarning("SSL certificate file not found at: {CertificatePath}", _certificatePath);
                }
            }

            // Extract path from webhook URL if it's set
            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                try
                {
                    var uri = new Uri(_webhookUrl);
                    _webhookPath = uri.AbsolutePath;
                    _logger.LogInformation("Using webhook path from URL: {Path}", _webhookPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract path from webhook URL, using empty path");
                    _webhookPath = "";
                }
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

                // 处理图片
                if (!string.IsNullOrEmpty(message.ImageUrl))
                {
                    try
                    {
                        // Format caption with HTML if needed
                        string caption = null;

                        // Send the photo using the URL directly
                        await _botClient.SendPhoto(
                            chatId: _chatId,
                            photo: InputFile.FromUri(new Uri(message.ImageUrl)),
                            caption: caption,
                            parseMode: caption != null ? ParseMode.Html : default
                        );

                        _logger.LogInformation("Image sent to Telegram: {Url}", message.ImageUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send image to Telegram: {Url}", message.ImageUrl);
                    }
                }

                // 处理文件
                if (!string.IsNullOrEmpty(message.FileUrl))
                {
                    try
                    {
                        // 根据文件类型选择发送方法
                        if (message.FileType == "video")
                        {
                            // 发送视频
                            await _botClient.SendVideo(
                                chatId: _chatId,
                                video: InputFile.FromUri(new Uri(message.FileUrl)),
                                caption: message.FileName
                            );
                            _logger.LogInformation("Video sent to Telegram: {FileName}, {Url}", message.FileName, message.FileUrl);
                        }
                        else if (message.FileType == "audio")
                        {
                            // 发送音频
                            await _botClient.SendAudio(
                                chatId: _chatId,
                                audio: InputFile.FromUri(new Uri(message.FileUrl)),
                                caption: message.FileName
                            );
                            _logger.LogInformation("Audio sent to Telegram: {FileName}, {Url}", message.FileName, message.FileUrl);
                        }
                        else if (message.FileType == "animation")
                        {
                            // 发送动画/GIF
                            await _botClient.SendAnimation(
                                chatId: _chatId,
                                animation: InputFile.FromUri(new Uri(message.FileUrl)),
                                caption: message.FileName
                            );
                            _logger.LogInformation("Animation sent to Telegram: {FileName}, {Url}", message.FileName, message.FileUrl);
                        }
                        else
                        {
                            // 发送普通文档
                            await _botClient.SendDocument(
                                chatId: _chatId,
                                document: InputFile.FromUri(new Uri(message.FileUrl)),
                                caption: message.FileName
                            );
                            _logger.LogInformation("Document sent to Telegram: {FileName}, {Url}", message.FileName, message.FileUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send file to Telegram: {FileName}, {Url}", message.FileName, message.FileUrl);

                        // 如果特定类型发送失败，尝试作为普通文档发送
                        try
                        {
                            await _botClient.SendDocument(
                                chatId: _chatId,
                                document: InputFile.FromUri(new Uri(message.FileUrl)),
                                caption: message.FileName
                            );
                            _logger.LogInformation("File sent as document to Telegram after type-specific method failed: {FileName}, {Url}",
                                message.FileName, message.FileUrl);
                        }
                        catch (Exception docEx)
                        {
                            _logger.LogError(docEx, "Failed to send file as document to Telegram: {FileName}, {Url}",
                                message.FileName, message.FileUrl);
                        }
                    }
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

                    // Validate webhook URL
                    if (!Uri.TryCreate(_webhookUrl, UriKind.Absolute, out Uri webhookUri))
                    {
                        _logger.LogError("Invalid webhook URL: {WebhookUrl}", _webhookUrl);
                        throw new InvalidOperationException($"Invalid webhook URL: {_webhookUrl}");
                    }

                    // Check if the URL uses HTTPS
                    if (webhookUri.Scheme != "https")
                    {
                        _logger.LogError("Webhook URL must use HTTPS: {WebhookUrl}", _webhookUrl);
                        throw new InvalidOperationException($"Webhook URL must use HTTPS: {_webhookUrl}");
                    }

                    // Set the webhook
                    // Make sure the webhook URL includes the port if it's not standard
                    string webhookUrlWithPort = _webhookUrl;

                    // If the URL doesn't include a port but we have a custom port configured
                    if ((webhookUri.Port == 80 || webhookUri.Port == 443) && _webhookPort != 80 && _webhookPort != 443 && _webhookPort != 0)
                    {
                        // Reconstruct the URL with the port
                        webhookUrlWithPort = $"{webhookUri.Scheme}://{webhookUri.Host}:{_webhookPort}{webhookUri.PathAndQuery}";
                        _logger.LogInformation("Adding port to webhook URL: {WebhookUrl}", webhookUrlWithPort);
                    }

                    // Log the path being used
                    _logger.LogInformation("Using webhook path: {Path}", webhookUri.AbsolutePath);

                    // Set the webhook with or without certificate
                    if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                    {
                        try
                        {
                            // Load the certificate
                            var certBytes = File.ReadAllBytes(_certificatePath);

                            // Create an InputFile from the certificate bytes
                            var inputFile = InputFile.FromStream(new MemoryStream(certBytes), "certificate.pem");

                            // Set the webhook with the certificate
                            _logger.LogInformation("Setting webhook with certificate from {CertificatePath}", _certificatePath);
                            await _botClient.SetWebhook(
                                url: webhookUrlWithPort,
                                certificate: inputFile
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to load or use certificate, falling back to standard webhook");
                            await _botClient.SetWebhook(webhookUrlWithPort);
                        }
                    }
                    else
                    {
                        // Set the webhook without certificate
                        await _botClient.SetWebhook(webhookUrlWithPort);
                    }

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

                // Use the path from the URL or the default path
                string path = string.IsNullOrEmpty(_webhookPath) ? "/" : _webhookPath;

                // Ensure the path ends with a slash for the HTTP listener
                if (!path.EndsWith('/'))
                {
                    path += "/";
                }

                string prefix = $"{uri.Scheme}://{uri.Host}:{port}{path}";

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
        /// Tests the Telegram webhook connectivity
        /// </summary>
        /// <returns>A string containing the test results</returns>
        public async Task<string> TestWebhookAsync()
        {
            try
            {
                if (_botClient == null)
                {
                    return "Telegram bot client is not initialized.";
                }

                _logger.LogInformation("Testing Telegram webhook connectivity");

                // Create a new cancellation token source for this operation
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 second timeout

                var result = new StringBuilder();
                result.AppendLine("=== Telegram Webhook Test ===");
                result.AppendLine();

                // Check if webhook is configured
                if (string.IsNullOrEmpty(_webhookUrl))
                {
                    result.AppendLine("Webhook is not configured. Please set a webhook URL in the configuration.");
                    return result.ToString();
                }

                // Validate webhook URL
                if (!Uri.TryCreate(_webhookUrl, UriKind.Absolute, out Uri webhookUri))
                {
                    result.AppendLine($"Invalid webhook URL: {_webhookUrl}");
                    result.AppendLine("The URL must be a valid absolute URL (e.g., https://example.com:8443)");
                    return result.ToString();
                }

                // Check if the URL uses HTTPS
                if (webhookUri.Scheme != "https")
                {
                    result.AppendLine($"Webhook URL must use HTTPS: {_webhookUrl}");
                    result.AppendLine("Telegram requires HTTPS for webhook URLs");
                    return result.ToString();
                }

                // Get current webhook info
                var webhookInfo = await _botClient.GetWebhookInfo(cts.Token);

                result.AppendLine("Current Webhook Configuration:");
                result.AppendLine($"- URL: {webhookInfo.Url}");
                result.AppendLine($"- Has Custom Certificate: {webhookInfo.HasCustomCertificate}");
                result.AppendLine($"- Pending Update Count: {webhookInfo.PendingUpdateCount}");
                result.AppendLine($"- Max Connections: {webhookInfo.MaxConnections}");
                result.AppendLine($"- IP Address: {webhookInfo.IpAddress ?? "Not set"}");

                if (webhookInfo.LastErrorDate != null)
                {
                    result.AppendLine($"- Last Error Date: {webhookInfo.LastErrorDate?.ToLocalTime()}");
                    result.AppendLine($"- Last Error Message: {webhookInfo.LastErrorMessage}");
                }
                else
                {
                    result.AppendLine("- No errors reported");
                }

                result.AppendLine();

                // Test HTTP listener
                if (_useWebhook)
                {
                    result.AppendLine("Local HTTP Listener Status:");

                    if (_httpListener != null && _httpListener.IsListening)
                    {
                        result.AppendLine("- HTTP Listener is running");

                        // Get the prefixes
                        var prefixes = string.Join(", ", _httpListener.Prefixes);
                        result.AppendLine($"- Listening on: {prefixes}");
                    }
                    else
                    {
                        result.AppendLine("- HTTP Listener is not running");

                        // Try to start the listener
                        result.AppendLine("- Attempting to start HTTP listener...");

                        try
                        {
                            await StartWebhookListenerAsync();
                            result.AppendLine("- Successfully started HTTP listener");
                        }
                        catch (Exception ex)
                        {
                            result.AppendLine($"- Failed to start HTTP listener: {ex.Message}");
                        }
                    }

                    result.AppendLine();
                }

                // Test webhook by setting it again
                result.AppendLine("Testing Webhook Connection:");

                try
                {
                    // We already have webhookUri from the validation above
                    string webhookUrlWithPort = _webhookUrl;

                    // If the URL doesn't include a port but we have a custom port configured
                    if ((webhookUri.Port == 80 || webhookUri.Port == 443) && _webhookPort != 80 && _webhookPort != 443 && _webhookPort != 0)
                    {
                        // Reconstruct the URL with the port
                        webhookUrlWithPort = $"{webhookUri.Scheme}://{webhookUri.Host}:{_webhookPort}{webhookUri.PathAndQuery}";
                        result.AppendLine($"- Adding port to webhook URL: {webhookUrlWithPort}");
                    }

                    // Log the path being used
                    result.AppendLine($"- Using webhook path: {webhookUri.AbsolutePath}");

                    // Check if we have a certificate
                    if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                    {
                        result.AppendLine($"- Found certificate at: {_certificatePath}");

                        try
                        {
                            // Load the certificate
                            var certBytes = File.ReadAllBytes(_certificatePath);

                            // Create an InputFile from the certificate bytes
                            var inputFile = InputFile.FromStream(new MemoryStream(certBytes), "certificate.pem");

                            // Set the webhook with the certificate
                            result.AppendLine("- Setting webhook with certificate");
                            await _botClient.SetWebhook(
                                url: webhookUrlWithPort,
                                certificate: inputFile,
                                cancellationToken: cts.Token
                            );
                            result.AppendLine("- Successfully set webhook with certificate");
                        }
                        catch (Exception ex)
                        {
                            result.AppendLine($"- Failed to use certificate: {ex.Message}");
                            result.AppendLine("- Falling back to standard webhook");
                            await _botClient.SetWebhook(webhookUrlWithPort, cancellationToken: cts.Token);
                            result.AppendLine("- Successfully set webhook without certificate");
                        }
                    }
                    else
                    {
                        // Set the webhook without certificate
                        if (!string.IsNullOrEmpty(_certificatePath))
                        {
                            result.AppendLine($"- Certificate not found at: {_certificatePath}");
                        }

                        await _botClient.SetWebhook(webhookUrlWithPort, cancellationToken: cts.Token);
                        result.AppendLine("- Successfully set webhook without certificate");
                    }

                    // Get updated webhook info
                    webhookInfo = await _botClient.GetWebhookInfo(cts.Token);

                    if (string.IsNullOrEmpty(webhookInfo.LastErrorMessage))
                    {
                        result.AppendLine("- Webhook is working correctly");
                    }
                    else
                    {
                        result.AppendLine($"- Webhook error: {webhookInfo.LastErrorMessage}");
                        result.AppendLine("- Please check your webhook configuration and server settings");
                    }
                }
                catch (Exception ex)
                {
                    result.AppendLine($"- Failed to set webhook: {ex.Message}");
                    result.AppendLine("- Please check your webhook configuration and server settings");
                }

                result.AppendLine();

                // Provide troubleshooting tips
                result.AppendLine("Troubleshooting Tips:");
                result.AppendLine("1. Make sure your server is publicly accessible");
                result.AppendLine("2. Ensure your server has a valid SSL certificate (required for webhooks)");
                result.AppendLine("3. Check that the port is open in your firewall");
                result.AppendLine("4. Verify that the webhook URL is correct and includes the port if needed");
                result.AppendLine("5. Telegram only allows webhooks on ports 443, 80, 88, and 8443");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Telegram webhook");
                return $"Error testing Telegram webhook: {ex.Message}";
            }
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

            // 处理各种类型的文件
            try
            {
                // 创建临时目录用于存储下载的文件
                string tempDir = Path.Combine(Path.GetTempPath(), "DC.QQ.TG", "TelegramFiles");
                Directory.CreateDirectory(tempDir);

                // 处理照片
                if (telegramMessage.Photo != null && telegramMessage.Photo.Length > 0)
                {
                    var fileId = telegramMessage.Photo[^1].FileId;
                    string fileName = $"photo_{DateTime.Now.Ticks}.jpg";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.ImageUrl = $"file://{filePath}";
                    _logger.LogDebug("Downloaded Telegram photo to: {FilePath}", filePath);
                }
                // 处理静态贴纸
                else if (telegramMessage.Sticker != null && telegramMessage.Sticker.IsAnimated == false)
                {
                    var fileId = telegramMessage.Sticker.FileId;
                    string fileName = $"sticker_{DateTime.Now.Ticks}.webp";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.ImageUrl = $"file://{filePath}";
                    _logger.LogDebug("Downloaded Telegram sticker to: {FilePath}", filePath);
                }
                // 处理文档
                else if (telegramMessage.Document != null)
                {
                    var fileId = telegramMessage.Document.FileId;
                    string fileName = telegramMessage.Document.FileName ?? $"document_{DateTime.Now.Ticks}";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.FileUrl = $"file://{filePath}";
                    message.FileName = fileName;
                    message.FileType = "document";
                    _logger.LogDebug("Downloaded Telegram document to: {FilePath}", filePath);
                }
                // 处理视频
                else if (telegramMessage.Video != null)
                {
                    var fileId = telegramMessage.Video.FileId;
                    string fileName = telegramMessage.Video.FileName ?? $"video_{DateTime.Now.Ticks}.mp4";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.FileUrl = $"file://{filePath}";
                    message.FileName = fileName;
                    message.FileType = "video";
                    _logger.LogDebug("Downloaded Telegram video to: {FilePath}", filePath);
                }
                // 处理音频
                else if (telegramMessage.Audio != null)
                {
                    var fileId = telegramMessage.Audio.FileId;
                    string fileName = telegramMessage.Audio.FileName ?? $"audio_{DateTime.Now.Ticks}.mp3";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.FileUrl = $"file://{filePath}";
                    message.FileName = fileName;
                    message.FileType = "audio";
                    _logger.LogDebug("Downloaded Telegram audio to: {FilePath}", filePath);
                }
                // 处理语音消息
                else if (telegramMessage.Voice != null)
                {
                    var fileId = telegramMessage.Voice.FileId;
                    string fileName = $"voice_{DateTime.Now.Ticks}.ogg";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.FileUrl = $"file://{filePath}";
                    message.FileName = fileName;
                    message.FileType = "audio";
                    _logger.LogDebug("Downloaded Telegram voice message to: {FilePath}", filePath);
                }
                // 处理动画/GIF
                else if (telegramMessage.Animation != null)
                {
                    var fileId = telegramMessage.Animation.FileId;
                    string fileName = telegramMessage.Animation.FileName ?? $"animation_{DateTime.Now.Ticks}.gif";

                    // 下载文件到临时目录
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, _cts.Token);

                    // 使用本地文件路径
                    message.FileUrl = $"file://{filePath}";
                    message.FileName = fileName;
                    message.FileType = "animation";
                    _logger.LogDebug("Downloaded Telegram animation to: {FilePath}", filePath);
                }

                // 临时文件清理由 FileDownloader 处理
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Telegram file attachment");

                // 如果下载失败，使用友好的错误消息而不是原始 URL
                string errorCode = ex.HResult.ToString("X8");

                // 根据异常类型确定文件类型和名称
                string fileType = "File";
                string fileName = "unknown";

                if (telegramMessage.Photo != null && telegramMessage.Photo.Length > 0)
                {
                    fileType = "Image";
                    fileName = "photo.jpg";
                }
                else if (telegramMessage.Sticker != null)
                {
                    fileType = "Sticker";
                    fileName = "sticker.webp";
                }
                else if (telegramMessage.Document != null)
                {
                    fileType = "Document";
                    fileName = telegramMessage.Document.FileName ?? "document";
                }
                else if (telegramMessage.Video != null)
                {
                    fileType = "Video";
                    fileName = telegramMessage.Video.FileName ?? "video.mp4";
                }
                else if (telegramMessage.Audio != null)
                {
                    fileType = "Audio";
                    fileName = telegramMessage.Audio.FileName ?? "audio.mp3";
                }
                else if (telegramMessage.Voice != null)
                {
                    fileType = "Voice";
                    fileName = "voice.ogg";
                }
                else if (telegramMessage.Animation != null)
                {
                    fileType = "Animation";
                    fileName = telegramMessage.Animation.FileName ?? "animation.gif";
                }

                message.Content += $"\n[FILE]\n{fileType}: {fileName}\ncode: {errorCode}";
                message.ImageUrl = null; // 清除图片 URL
                message.FileUrl = null; // 清除文件 URL
            }

            _logger.LogInformation("Received message from Telegram: {Message}", messageText);
            MessageReceived?.Invoke(this, message);
        }

        /// <summary>
        /// 下载 Telegram 文件到本地临时目录
        /// </summary>
        private async Task<string> DownloadTelegramFileAsync(string fileId, string fileName, CancellationToken cancellationToken)
        {
            try
            {
                // 获取文件信息
                var fileInfo = await _botClient.GetFile(fileId, cancellationToken);

                // 构建 Telegram 文件 URL
                var token = _configuration["Telegram:BotToken"];
                var url = $"https://api.telegram.org/file/bot{token}/{fileInfo.FilePath}";

                // 使用 FileDownloader 下载文件
                string localUrl = await FileDownloader.DownloadFileAsync(url, fileName, _logger, cancellationToken);

                // 从 file:// URL 中提取本地文件路径
                string filePath = localUrl.StartsWith("file://") ? localUrl["file://".Length..] : localUrl;

                _logger.LogDebug("Downloaded Telegram file to: {FilePath}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading Telegram file: {FileId}", fileId);
                throw;
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

                    // 下载头像到临时目录
                    string fileName = $"avatar_{user.Id}_{DateTime.Now.Ticks}.jpg";
                    string filePath = await DownloadTelegramFileAsync(fileId, fileName, cancellationToken);

                    // 返回本地文件路径
                    string avatarUrl = $"file://{filePath}";
                    _logger.LogInformation("Successfully downloaded Telegram avatar for user {UserId} to {FilePath}", user.Id, filePath);
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
