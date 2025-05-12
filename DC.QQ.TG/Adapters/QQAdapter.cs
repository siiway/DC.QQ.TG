using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DC.QQ.TG.Utils;
using System.Collections.Concurrent;

namespace DC.QQ.TG.Adapters
{
    public class QQAdapter : IMessageAdapter, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<QQAdapter> _logger;
        private readonly HttpClient _httpClient;
        private Timer? _pollingTimer;
        private string? _lastMessageId;
        private bool _isListening;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _webSocketCts;
        private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();
        private Task? _receiveTask;
        private Task? _sendTask;
        private bool _useWebSocket;
        private Dictionary<string, string> _qqNicknameCache = new Dictionary<string, string>();

        public MessageSource Platform => MessageSource.QQ;

        public event EventHandler<Message>? MessageReceived;

        public QQAdapter(IConfiguration configuration, ILogger<QQAdapter> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        private string? _groupId;

        public async Task InitializeAsync()
        {
            var baseUrl = _configuration["NapCat:BaseUrl"];
            var token = _configuration["NapCat:Token"];
            _groupId = _configuration["NapCat:GroupId"];

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_groupId))
            {
                throw new InvalidOperationException("NapCat configuration is missing or invalid");
            }

            // Check if we should use WebSocket
            _useWebSocket = baseUrl.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ||
                            baseUrl.StartsWith("wss:", StringComparison.OrdinalIgnoreCase);

            if (_useWebSocket)
            {
                _logger.LogInformation("Using WebSocket protocol for NapCat QQ");

                // Initialize WebSocket connection
                await InitializeWebSocketAsync(baseUrl, token);
            }
            else
            {
                _logger.LogInformation("Using HTTP protocol for NapCat QQ");

                // Initialize HTTP connection
                _httpClient.BaseAddress = new Uri(baseUrl);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                // Test the connection
                await TestHttpConnectionAsync();
            }
        }

        private async Task InitializeWebSocketAsync(string baseUrl, string token)
        {
            try
            {
                _webSocketCts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                // Add authorization header
                _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");

                // Connect to the WebSocket server
                await _webSocket.ConnectAsync(new Uri(baseUrl), _webSocketCts.Token);

                _logger.LogInformation("Connected to NapCat WebSocket: {Url}", baseUrl);

                // Start receive and send tasks
                _receiveTask = Task.Run(() => ReceiveWebSocketMessagesAsync(_webSocketCts.Token));
                _sendTask = Task.Run(() => SendWebSocketMessagesAsync(_webSocketCts.Token));

                // Verify the group exists
                await SendWebSocketCommandAsync(new
                {
                    action = "get_group_list",
                    @params = new { }
                });

                // Get group members to build nickname cache
                await GetGroupMembersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to NapCat WebSocket");
                throw;
            }
        }

        private Task SendWebSocketCommandAsync(object command)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                _logger.LogWarning("Cannot send command: WebSocket is not open");
                return Task.CompletedTask;
            }

            string json = JsonConvert.SerializeObject(command);
            _sendQueue.Enqueue(json);

            // Check if we should show NapCat responses
            bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";
            if (showNapCatResponse)
            {
                _logger.LogInformation("Queued WebSocket command: {Command}", json);
            }

            return Task.CompletedTask;
        }

        private async Task SendWebSocketMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    if (_sendQueue.TryDequeue(out string? message) && message != null)
                    {
                        var buffer = Encoding.UTF8.GetBytes(message);
                        await _webSocket.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);

                        // Check if we should show NapCat responses
                        bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";
                        if (showNapCatResponse)
                        {
                            _logger.LogInformation("Sent WebSocket message: {Message}", message);
                        }
                    }
                    else
                    {
                        // No messages to send, wait a bit
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket send loop");
            }
        }

        private async Task ReceiveWebSocketMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket closed by server");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        // Check if we should show NapCat responses
                        bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";
                        if (showNapCatResponse)
                        {
                            _logger.LogInformation("Received WebSocket message: {Message}", message);
                        }

                        // Process the message
                        await ProcessWebSocketMessageAsync(message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket receive loop");
            }
        }

        private Task ProcessWebSocketMessageAsync(string message)
        {
            try
            {
                var response = JObject.Parse(message);

                // Check if this is a group message event
                if (response["post_type"]?.ToString() == "message" &&
                    response["message_type"]?.ToString() == "group" &&
                    response["group_id"]?.ToString() == _groupId)
                {
                    var messageId = response["message_id"]?.ToString();

                    // Skip if we've already processed this message or message ID is null
                    if (string.IsNullOrEmpty(messageId) || messageId == _lastMessageId)
                        return Task.CompletedTask;

                    _lastMessageId = messageId;

                    // Parse the message content
                    string messageContent = ParseQQMessageContent(response["message"]);

                    // Get user ID for avatar URL
                    string userId = response["sender"]?["user_id"]?.ToString() ?? "Unknown";

                    var qqMessage = new Message
                    {
                        Id = messageId,
                        Content = messageContent,
                        SenderName = response["sender"]?["nickname"]?.ToString() ?? "Unknown",
                        SenderId = userId,
                        Source = MessageSource.QQ,
                        Timestamp = response["time"] != null && long.TryParse(response["time"]?.ToString(), out long timestamp)
                            ? DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime
                            : DateTime.Now,
                        AvatarUrl = GetQQAvatarUrl(userId)
                    };

                    _logger.LogDebug("Received message from QQ group {GroupId}: {Message}",
                        _groupId, qqMessage.Content);

                    MessageReceived?.Invoke(this, qqMessage);
                }
                // Check if this is a response to get_group_list or get_group_member_list
                else if (response["data"] != null && response["status"]?.ToString() == "ok")
                {
                    // Check if we should show NapCat responses
                    bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat WebSocket Response: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    // Check if this is a response to get_group_member_list
                    if (response["echo"]?.ToString() == "get_group_member_list" ||
                        (response["data"] != null && (response["data"]?["group_id"]?.ToString() == _groupId)))
                    {
                        // Process group member list response
                        ProcessGroupMemberListResponse(response);
                    }
                    // Otherwise, assume it's a response to get_group_list
                    else
                    {
                        // Process group list response
                        ProcessGroupListResponse(response);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WebSocket message: {Message}", message);
            }

            return Task.CompletedTask;
        }

        private void ProcessGroupListResponse(JObject response)
        {
            try
            {
                // Check the structure of the response
                JArray? groups = null;

                // The response might have different structures depending on the API version
                if (response["data"] is JArray dataArray)
                {
                    // If data is directly an array
                    groups = dataArray;
                    _logger.LogDebug("NapCat API returned data as JArray");
                }
                else if (response["data"] is JObject dataObj)
                {
                    if (dataObj["list"] is JArray listArray)
                    {
                        // If data contains a list property that is an array
                        groups = listArray;
                        _logger.LogDebug("NapCat API returned data.list as JArray");
                    }
                    else
                    {
                        _logger.LogWarning("NapCat API returned data as JObject but without a list property that is a JArray");
                    }
                }
                else
                {
                    _logger.LogWarning("Unexpected response format from NapCat API. Data structure is not recognized.");
                }

                bool groupFound = false;

                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        var groupId = group["group_id"]?.ToString();
                        if (groupId == _groupId)
                        {
                            groupFound = true;
                            _logger.LogInformation("Found QQ group: {GroupName} (ID: {GroupId})",
                                group["group_name"], groupId);
                            break;
                        }
                    }

                    if (!groupFound)
                    {
                        _logger.LogWarning("QQ group with ID {GroupId} not found", _groupId);

                        // Log available groups
                        _logger.LogInformation("Available groups:");
                        foreach (var group in groups)
                        {
                            _logger.LogInformation("  - {GroupName} (ID: {GroupId})",
                                group["group_name"], group["group_id"]);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No groups found in the NapCat API response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing group list response");
            }
        }

        /// <summary>
        /// Gets the group members and builds a cache of QQ IDs to nicknames
        /// </summary>
        private async Task GetGroupMembersAsync()
        {
            try
            {
                _logger.LogInformation("Getting members for QQ group {GroupId}", _groupId);

                if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                {
                    // Send via WebSocket
                    await SendWebSocketCommandAsync(new
                    {
                        action = "get_group_member_list",
                        @params = new
                        {
                            group_id = _groupId
                        }
                    });

                    // The response will be handled in ProcessWebSocketMessageAsync
                    // We'll add a delay to give time for the response to be processed
                    await Task.Delay(1000);
                }
                else
                {
                    // Send via HTTP
                    var response = await _httpClient.PostJsonAsync<JObject>("/get_group_member_list", new
                    {
                        group_id = _groupId
                    });

                    // Check if we should show NapCat responses
                    bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";
                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat API Group Member List Response: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    if (response["status"]?.ToString() == "ok")
                    {
                        ProcessGroupMemberListResponse(response);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting group members for QQ group {GroupId}", _groupId);
            }
        }

        /// <summary>
        /// Processes the response from get_group_member_list and builds the nickname cache
        /// </summary>
        private void ProcessGroupMemberListResponse(JObject response)
        {
            try
            {
                // Clear the existing cache
                _qqNicknameCache.Clear();

                // Check the structure of the response
                JArray? members = null;

                // The response might have different structures depending on the API version
                if (response["data"] is JArray dataArray)
                {
                    // If data is directly an array
                    members = dataArray;
                    _logger.LogDebug("NapCat API returned member data as JArray");
                }
                else if (response["data"] is JObject dataObj)
                {
                    if (dataObj["list"] is JArray listArray)
                    {
                        // If data contains a list property that is an array
                        members = listArray;
                        _logger.LogDebug("NapCat API returned data.list as JArray for members");
                    }
                    else
                    {
                        _logger.LogWarning("NapCat API returned data as JObject but without a list property that is a JArray for members");
                    }
                }
                else
                {
                    _logger.LogWarning("Unexpected response format from NapCat API for members. Data structure is not recognized.");
                }

                if (members != null)
                {
                    int memberCount = 0;

                    foreach (var member in members)
                    {
                        var userId = member["user_id"]?.ToString();

                        // Try to get the card (group nickname) first, then fall back to nickname
                        var nickname = member["card"]?.ToString();
                        if (string.IsNullOrEmpty(nickname))
                        {
                            nickname = member["nickname"]?.ToString();
                        }

                        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(nickname))
                        {
                            _qqNicknameCache[userId] = nickname;
                            memberCount++;
                        }
                    }

                    _logger.LogInformation("Cached {Count} QQ group member nicknames", memberCount);
                }
                else
                {
                    _logger.LogWarning("No members found in the NapCat API response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing group member list response");
            }
        }

        private async Task TestHttpConnectionAsync()
        {
            try
            {
                var response = await _httpClient.PostJsonAsync<JObject>("/get_status", new { });

                // Check if we should show NapCat responses
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                if (showNapCatResponse)
                {
                    _logger.LogInformation("NapCat API Status Response: {Response}",
                        response.ToString(Formatting.Indented));
                }

                _logger.LogInformation("Connected to NapCat: {Status}", response["status"]);

                // Verify the group exists
                var groupsResponse = await _httpClient.PostJsonAsync<JObject>("/get_group_list", new { });

                if (showNapCatResponse)
                {
                    _logger.LogInformation("NapCat API Group List Response: {Response}",
                        groupsResponse.ToString(Formatting.Indented));
                }

                if (groupsResponse["status"]?.ToString() == "ok")
                {
                    // Process group list response
                    ProcessGroupListResponse(groupsResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to NapCat");
                throw;
            }
        }

        public async Task SendMessageAsync(Message message)
        {
            try
            {
                // Format the message with source and sender info
                string formattedMessage = $"{message.GetFormattedUsername()}: {message.Content}";

                // Check if we should show NapCat responses
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                {
                    // Send via WebSocket
                    await SendWebSocketCommandAsync(new
                    {
                        action = "send_group_msg",
                        @params = new
                        {
                            group_id = _groupId,
                            message = formattedMessage
                        }
                    });

                    _logger.LogInformation("Message sent to QQ group {GroupId} via WebSocket", _groupId);
                }
                else
                {
                    // Send via HTTP
                    var response = await _httpClient.PostJsonAsync<JObject>("/send_group_msg", new
                    {
                        group_id = _groupId,
                        message = formattedMessage
                    });

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat API Send Response: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    _logger.LogInformation("Message sent to QQ group {GroupId} via HTTP", _groupId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to QQ group {GroupId}", _groupId);
            }
        }

        public Task StartListeningAsync()
        {
            if (_isListening)
                return Task.CompletedTask;

            _isListening = true;

            if (_useWebSocket)
            {
                // WebSocket is already listening in the background
                _logger.LogInformation("WebSocket is already listening for messages");
            }
            else
            {
                // Start HTTP polling
                _pollingTimer = new Timer(PollMessages, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
                _logger.LogInformation("Started HTTP polling for messages");
            }

            return Task.CompletedTask;
        }

        public async Task StopListeningAsync()
        {
            _isListening = false;

            if (_useWebSocket)
            {
                // Stop WebSocket
                if (_webSocketCts != null)
                {
                    _webSocketCts.Cancel();
                    _webSocketCts.Dispose();
                    _webSocketCts = null;
                }

                if (_webSocket != null)
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    _webSocket.Dispose();
                    _webSocket = null;
                }

                _logger.LogInformation("Stopped WebSocket connection");
            }
            else
            {
                // Stop HTTP polling
                if (_pollingTimer != null)
                {
                    _pollingTimer.Dispose();
                    _pollingTimer = null;
                }
                _logger.LogInformation("Stopped HTTP polling");
            }

            return;
        }

        public void Dispose()
        {
            // Clean up resources
            _webSocketCts?.Dispose();
            _webSocket?.Dispose();
            _pollingTimer?.Dispose();

            // Suppress finalization
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets the avatar URL for a QQ user
        /// </summary>
        private string GetQQAvatarUrl(string userId)
        {
            // If user ID is unknown or invalid, return default avatar
            if (string.IsNullOrEmpty(userId) || userId == "Unknown")
            {
                _logger.LogWarning("Failed to get QQ avatar: Invalid user ID. Using default avatar.");
                return "https://avatars.githubusercontent.com/u/197464182";
            }

            // QQ avatar URL format
            string avatarUrl = $"https://q1.qlogo.cn/g?b=qq&nk={userId}&s=640";
            _logger.LogInformation("Successfully retrieved QQ avatar URL: {AvatarUrl}", avatarUrl);
            return avatarUrl;
        }

        /// <summary>
        /// Parses QQ message content from various formats into a readable string
        /// </summary>
        private string ParseQQMessageContent(JToken? messageToken)
        {
            try
            {
                // If the message is null, return empty string
                if (messageToken == null)
                    return string.Empty;

                // If the message is a simple string, return it directly
                if (messageToken.Type == JTokenType.String)
                    return messageToken.ToString();

                // Check if the message is a JSON array (complex message with multiple parts)
                if (messageToken.Type == JTokenType.Array)
                {
                    var messageArray = (JArray)messageToken;
                    var contentBuilder = new StringBuilder();

                    foreach (var part in messageArray)
                    {
                        // Each part should be an object with 'type' and 'data' properties
                        if (part["type"] != null && part["data"] != null)
                        {
                            string type = part["type"]?.ToString() ?? "unknown";

                            switch (type)
                            {
                                case "text":
                                    // For text type, extract the text content
                                    if (part["data"]?["text"] != null)
                                    {
                                        contentBuilder.Append(part["data"]?["text"]?.ToString() ?? "");
                                    }
                                    break;

                                case "image":
                                    // For image type, add a placeholder
                                    contentBuilder.Append("[Image]");
                                    break;

                                case "face":
                                    // For face/emoji type, add a placeholder
                                    contentBuilder.Append("[Emoji]");
                                    break;

                                case "at":
                                    // For @mentions, add the mention with nickname if available
                                    if (part["data"]?["qq"] != null)
                                    {
                                        string qqId = part["data"]?["qq"]?.ToString() ?? "someone";
                                        string nickname;

                                        // First try to get the nickname from our cache
                                        if (_qqNicknameCache.TryGetValue(qqId, out string? cachedNickname) && !string.IsNullOrEmpty(cachedNickname))
                                        {
                                            nickname = cachedNickname;
                                            _logger.LogDebug("Using cached nickname for QQ user {QQId}: {Nickname}", qqId, nickname);
                                        }
                                        else
                                        {
                                            // If not in cache, try to get the nickname from the data if available
                                            nickname = part["data"]?["name"]?.ToString() ?? "";

                                            // If nickname is not available in the data, use the QQ ID
                                            if (string.IsNullOrEmpty(nickname))
                                            {
                                                nickname = qqId;
                                                _logger.LogDebug("No nickname found for QQ user {QQId}, using ID", qqId);
                                            }
                                            else
                                            {
                                                _logger.LogDebug("Using nickname from message data for QQ user {QQId}: {Nickname}", qqId, nickname);
                                            }

                                            // Add to cache for future use
                                            if (!string.IsNullOrEmpty(nickname) && nickname != qqId)
                                            {
                                                _qqNicknameCache[qqId] = nickname;
                                                _logger.LogDebug("Added nickname to cache for QQ user {QQId}: {Nickname}", qqId, nickname);
                                            }
                                        }

                                        contentBuilder.Append($"@{nickname} ");
                                    }
                                    break;

                                default:
                                    // For other types, add a placeholder with the type
                                    contentBuilder.Append($"[{type}]");
                                    break;
                            }
                        }
                    }

                    return contentBuilder.ToString();
                }

                // If it's a JSON object, try to extract meaningful information
                if (messageToken.Type == JTokenType.Object)
                {
                    // Log the unexpected format for debugging
                    _logger.LogDebug("Unexpected message format (object): {Message}",
                        messageToken.ToString(Formatting.None));

                    // Try to find any text content
                    if (messageToken["text"] != null)
                        return messageToken["text"]?.ToString() ?? "";

                    // If no text found, return the raw JSON
                    return messageToken.ToString(Formatting.None);
                }

                // For any other format, return the raw string
                return messageToken.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QQ message content");
                return "[Error parsing message]";
            }
        }

        private async void PollMessages(object? state)
        {
            try
            {
                // Get recent messages from the specified group
                var response = await _httpClient.PostJsonAsync<JObject>("/get_group_msg", new
                {
                    group_id = _groupId
                });

                // Check if we should show NapCat responses
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";
                if (showNapCatResponse)
                {
                    _logger.LogInformation("NapCat API Response: {Response}",
                        response.ToString(Formatting.Indented));
                }

                if (response["status"]?.ToString() == "ok")
                {
                    // Check the structure of the response
                    JArray? messages = null;

                    // The response might have different structures depending on the API version
                    if (response["data"] is JArray dataArray)
                    {
                        // If data is directly an array
                        messages = dataArray;
                        _logger.LogDebug("NapCat API returned messages data as JArray");
                    }
                    else if (response["data"] is JObject dataObj)
                    {
                        if (dataObj["messages"] is JArray messagesArray)
                        {
                            // If data contains a messages property that is an array
                            messages = messagesArray;
                            _logger.LogDebug("NapCat API returned data.messages as JArray");
                        }
                        else if (dataObj["list"] is JArray listArray)
                        {
                            // If data contains a list property that is an array
                            messages = listArray;
                            _logger.LogDebug("NapCat API returned data.list as JArray");
                        }
                        else
                        {
                            _logger.LogWarning("NapCat API returned data as JObject but without a messages or list property that is a JArray");
                            if (showNapCatResponse)
                            {
                                _logger.LogDebug("Data structure: {Data}", response["data"]?.ToString(Formatting.Indented) ?? "null");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unexpected response format from NapCat API. Data structure is not recognized.");
                        if (showNapCatResponse)
                        {
                            _logger.LogDebug("Data structure: {Data}", response["data"]?.ToString() ?? "null");
                        }
                    }

                    if (messages != null)
                    {
                        foreach (var msg in messages)
                        {
                            var messageId = msg["message_id"]?.ToString();

                            // Skip if we've already processed this message or message ID is null
                            if (string.IsNullOrEmpty(messageId) || messageId == _lastMessageId)
                                continue;

                            _lastMessageId = messageId;

                            // Check if this is a group message
                            if (msg["message_type"]?.ToString() == "group" &&
                                msg["group_id"]?.ToString() == _groupId)
                            {
                                // Parse the message content
                                string messageContent = ParseQQMessageContent(msg["message"]);

                                // Get user ID for avatar URL
                                string userId = msg["sender"]?["user_id"]?.ToString() ?? "Unknown";

                                var message = new Message
                                {
                                    Id = messageId,
                                    Content = messageContent,
                                    SenderName = msg["sender"]?["nickname"]?.ToString() ?? "Unknown",
                                    SenderId = userId,
                                    Source = MessageSource.QQ,
                                    Timestamp = msg["time"] != null && long.TryParse(msg["time"]?.ToString(), out long timestamp)
                                        ? DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime
                                        : DateTime.Now,
                                    AvatarUrl = GetQQAvatarUrl(userId)
                                };

                                _logger.LogDebug("Received message from QQ group {GroupId}: {Message}",
                                    _groupId, message.Content);

                                MessageReceived?.Invoke(this, message);
                            }
                        }
                    }
                    else if (showNapCatResponse)
                    {
                        _logger.LogWarning("NapCat API returned no messages array in response");
                    }
                }
                else if (showNapCatResponse)
                {
                    _logger.LogWarning("NapCat API returned non-OK status: {Status}",
                        response["status"]?.ToString() ?? "null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling QQ messages from group {GroupId}", _groupId);
            }
        }
    }
}
