using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using DC.QQ.TG.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using Telegram.Bot;

namespace DC.QQ.TG.Services
{
    public class ValidationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ValidationService> _logger;
        private readonly HttpClient _httpClient;

        public ValidationService(IConfiguration configuration, ILogger<ValidationService> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<bool> ValidateAllServicesAsync()
        {
            bool isValid = true;
            bool discordDisabled = _configuration["Disabled:Discord"]?.ToLower() == "true";
            bool telegramDisabled = _configuration["Disabled:Telegram"]?.ToLower() == "true";
            bool qqDisabled = _configuration["Disabled:QQ"]?.ToLower() == "true";

            _logger.LogInformation("Starting validation of service configurations...");

            // Validate Discord webhook if not disabled
            if (!discordDisabled)
            {
                _logger.LogInformation("Validating Discord webhook...");
                if (!await ValidateDiscordWebhookAsync())
                {
                    _logger.LogError("Discord webhook validation failed");
                    isValid = false;
                }
                else
                {
                    _logger.LogInformation("SUCCESS: Discord webhook validation successful");
                }
            }
            else
            {
                _logger.LogInformation("NOTICE: Discord validation skipped (platform disabled)");
            }

            // Validate Telegram bot token if not disabled
            if (!telegramDisabled)
            {
                _logger.LogInformation("Validating Telegram bot...");
                if (!await ValidateTelegramBotAsync())
                {
                    _logger.LogError("Telegram bot validation failed");
                    isValid = false;
                }
                else
                {
                    _logger.LogInformation("SUCCESS: Telegram bot validation successful");
                }
            }
            else
            {
                _logger.LogInformation("NOTICE: Telegram validation skipped (platform disabled)");
            }

            // Validate NapCat API if not disabled
            if (!qqDisabled)
            {
                _logger.LogInformation("Validating QQ (NapCat) API...");
                if (!await ValidateNapCatApiAsync())
                {
                    _logger.LogError("QQ (NapCat) API validation failed");
                    isValid = false;
                }
                else
                {
                    _logger.LogInformation("SUCCESS: QQ (NapCat) API validation successful");
                }
            }
            else
            {
                _logger.LogInformation("NOTICE: QQ validation skipped (platform disabled)");
            }

            if (isValid)
            {
                _logger.LogInformation("SUCCESS: All enabled services validated successfully");
            }
            else
            {
                _logger.LogError("ERROR: Service validation failed. Please check your configuration.");
            }

            return isValid;
        }

        private async Task<bool> ValidateDiscordWebhookAsync()
        {
            var webhookUrl = _configuration["Discord:WebhookUrl"];
            var botToken = _configuration["Discord:BotToken"];
            var autoWebhook = _configuration["Discord:AutoWebhook"]?.ToLower() == "true";

            // If webhook URL is missing but auto-webhook is enabled and bot token is provided,
            // we'll create a webhook later, so validation passes
            if (string.IsNullOrEmpty(webhookUrl))
            {
                if (autoWebhook && !string.IsNullOrEmpty(botToken))
                {
                    _logger.LogInformation("Discord webhook URL is missing, but auto-webhook is enabled and bot token is provided");
                    _logger.LogInformation("A webhook will be created automatically during initialization");
                    return true;
                }
                else
                {
                    _logger.LogError("Discord webhook URL is missing and auto-webhook is not enabled or bot token is missing");
                    return false;
                }
            }

            try
            {
                _logger.LogDebug("Sending GET request to Discord webhook URL: {WebhookUrl}",
                    webhookUrl.Substring(0, Math.Min(30, webhookUrl.Length)) + "...");

                // Discord webhooks accept GET requests to validate them
                var response = await _httpClient.GetAsync(webhookUrl);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Discord webhook response: {StatusCode}", response.StatusCode);
                    return true;
                }
                else
                {
                    _logger.LogError("Discord webhook validation failed with status code: {StatusCode}", response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        _logger.LogDebug("Discord webhook error response: {Content}", content);
                    }

                    // If webhook validation failed but auto-webhook is enabled and bot token is provided,
                    // we'll create a new webhook later, so validation passes
                    if (autoWebhook && !string.IsNullOrEmpty(botToken))
                    {
                        _logger.LogInformation("Discord webhook validation failed, but auto-webhook is enabled and bot token is provided");
                        _logger.LogInformation("A new webhook will be created automatically during initialization");
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discord webhook validation failed with exception");

                // If webhook validation failed but auto-webhook is enabled and bot token is provided,
                // we'll create a new webhook later, so validation passes
                if (autoWebhook && !string.IsNullOrEmpty(botToken))
                {
                    _logger.LogInformation("Discord webhook validation failed, but auto-webhook is enabled and bot token is provided");
                    _logger.LogInformation("A new webhook will be created automatically during initialization");
                    return true;
                }

                return false;
            }
        }

        private async Task<bool> ValidateTelegramBotAsync()
        {
            var botToken = _configuration["Telegram:BotToken"];
            var chatId = _configuration["Telegram:ChatId"];

            if (string.IsNullOrEmpty(botToken))
            {
                _logger.LogError("Telegram bot token is missing");
                return false;
            }

            if (string.IsNullOrEmpty(chatId))
            {
                _logger.LogError("Telegram chat ID is missing");
                return false;
            }

            try
            {
                _logger.LogDebug("Creating Telegram bot client with token: {BotToken}...",
                    botToken.Substring(0, Math.Min(10, botToken.Length)) + "...");

                // Create a Telegram bot client and get the bot info
                var botClient = new TelegramBotClient(botToken);

                _logger.LogDebug("Requesting Telegram bot information...");
                var me = await botClient.GetMeAsync();

                _logger.LogInformation("Telegram bot found: @{Username} (ID: {BotId})",
                    me.Username, me.Id);

                // Try to get chat info to validate chat ID
                try
                {
                    _logger.LogDebug("Validating Telegram chat ID: {ChatId}", chatId);
                    var chat = await botClient.GetChatAsync(chatId);

                    string chatName = chat.Title ?? chat.Username ?? "Private Chat";
                    string chatType = chat.Type.ToString();

                    _logger.LogInformation("Telegram chat found: {ChatName} (Type: {ChatType})",
                        chatName, chatType);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Telegram chat ID validation failed: {Message}", ex.Message);
                    _logger.LogDebug(ex, "Telegram chat validation exception details");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Telegram bot token validation failed: {Message}", ex.Message);
                _logger.LogDebug(ex, "Telegram bot validation exception details");
                return false;
            }
        }

        private async Task<bool> ValidateNapCatApiAsync()
        {
            var baseUrl = _configuration["NapCat:BaseUrl"];
            var token = _configuration["NapCat:Token"];
            var groupId = _configuration["NapCat:GroupId"];

            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("NapCat base URL is missing");
                return false;
            }

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("NapCat token is missing");
                return false;
            }

            if (string.IsNullOrEmpty(groupId))
            {
                _logger.LogError("QQ group ID is missing");
                return false;
            }

            try
            {
                // Check if the URL is a WebSocket URL
                bool isWebSocket = baseUrl.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ||
                                  baseUrl.StartsWith("wss:", StringComparison.OrdinalIgnoreCase);

                if (isWebSocket)
                {
                    _logger.LogInformation("WebSocket URL detected for NapCat API: {BaseUrl}", baseUrl);

                    // Validate the WebSocket URL
                    try
                    {
                        // Create a ClientWebSocket to test the connection
                        using var webSocket = new ClientWebSocket();

                        // Add authorization header
                        webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");

                        // Set a timeout for the connection attempt
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        _logger.LogDebug("Attempting to connect to WebSocket at {BaseUrl}...", baseUrl);

                        // Try to connect to the WebSocket server
                        await webSocket.ConnectAsync(new Uri(baseUrl), cts.Token);

                        // If we get here, the connection was successful
                        _logger.LogInformation("WebSocket connection successful");

                        // Close the connection properly
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Validation complete", CancellationToken.None);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Failed to connect to WebSocket: {Message}", ex.Message);
                        _logger.LogDebug(ex, "WebSocket connection exception details");
                        return false;
                    }
                }


                _logger.LogWarning("HTTP URL detected for NapCat API: {BaseUrl}. Incoming messages will NOT work.", baseUrl);
                _logger.LogDebug("Creating HTTP client for NapCat API at {BaseUrl}", baseUrl);

                // Create a temporary HttpClient for validation
                var client = new HttpClient
                {
                    BaseAddress = new Uri(baseUrl)
                };
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                // Test the connection
                _logger.LogDebug("Sending status request to NapCat API...");
                var response = await client.PostJsonAsync<JObject>("/get_status", new { });

                if (response["status"]?.ToString() == "ok")
                {
                    _logger.LogInformation("NapCat API connection successful");

                    // Verify the group exists
                    _logger.LogDebug("Requesting group list from NapCat API...");
                    var groupsResponse = await client.PostJsonAsync<JObject>("/get_group_list", new { });

                    if (groupsResponse["status"]?.ToString() == "ok")
                    {
                        // Check the structure of the response
                        JArray? groups = null;

                        // The response might have different structures depending on the API version
                        if (groupsResponse["data"] is JArray dataArray)
                        {
                            // If data is directly an array
                            groups = dataArray;
                        }
                        else if (groupsResponse["data"] is JObject dataObj && dataObj["list"] is JArray listArray)
                        {
                            // If data contains a list property that is an array
                            groups = listArray;
                        }
                        else
                        {
                            _logger.LogWarning("Unexpected response format from NapCat API. Data structure is not recognized.");
                            _logger.LogDebug("Response: {Response}", groupsResponse.ToString(Formatting.Indented));
                        }

                        bool groupFound = false;

                        if (groups != null)
                        {
                            foreach (var group in groups)
                            {
                                var id = group["group_id"]?.ToString();
                                if (id == groupId)
                                {
                                    groupFound = true;
                                    _logger.LogInformation("QQ group found: {GroupName} (ID: {GroupId})",
                                        group["group_name"], id);
                                    break;
                                }
                            }
                        }

                        if (!groupFound)
                        {
                            _logger.LogWarning("QQ group with ID {GroupId} not found in the group list", groupId);
                            _logger.LogInformation("Available groups:");

                            if (groups != null && groups.Count > 0)
                            {
                                foreach (var group in groups)
                                {
                                    _logger.LogInformation("  - {GroupName} (ID: {GroupId})",
                                        group["group_name"], group["group_id"]);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("  No groups available");
                            }

                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to get group list from NapCat API: {Status}",
                            groupsResponse["status"]);
                    }

                    return true;
                }
                else
                {
                    _logger.LogError("NapCat API validation failed with status: {Status}",
                        response["status"] ?? "unknown");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("NapCat API validation failed: {Message}", ex.Message);
                _logger.LogDebug(ex, "NapCat API validation exception details");
                return false;
            }
        }
    }
}
