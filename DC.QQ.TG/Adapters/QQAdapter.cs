using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string? _lastImageUrl;
        private string? _lastFileUrl;
        private string? _lastFileName;
        private string? _lastFileType;
        private readonly Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new Dictionary<string, TaskCompletionSource<string>>();

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

        private async Task ProcessWebSocketMessageAsync(string message)
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
                        return;

                    _lastMessageId = messageId;

                    // Parse the message content
                    string messageContent = ParseQQMessageContent(response["message"]);

                    // Replace @ placeholders with actual nicknames
                    messageContent = await ReplaceQQUserPlaceholdersAsync(messageContent);

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

                    // 添加图片 URL（如果有）
                    if (!string.IsNullOrEmpty(_lastImageUrl))
                    {
                        qqMessage.ImageUrl = _lastImageUrl;
                        _logger.LogInformation("Added image URL to QQ message: {Url}", _lastImageUrl);
                        _lastImageUrl = null; // 清除，避免重复使用
                    }

                    // 添加文件 URL（如果有）
                    if (!string.IsNullOrEmpty(_lastFileUrl))
                    {
                        qqMessage.FileUrl = _lastFileUrl;
                        qqMessage.FileName = _lastFileName;
                        qqMessage.FileType = _lastFileType;
                        _logger.LogInformation("Added file URL to QQ message: {FileName}, {FileType}, {Url}",
                            _lastFileName, _lastFileType, _lastFileUrl);

                        // 清除，避免重复使用
                        _lastFileUrl = null;
                        _lastFileName = null;
                        _lastFileType = null;
                    }

                    _logger.LogDebug("Received message from QQ group {GroupId}: {Message}",
                        _groupId, qqMessage.Content);

                    MessageReceived?.Invoke(this, qqMessage);
                }
                // Check if this is a response to get_stranger_info
                else if (response["echo"]?.ToString()?.StartsWith("get_stranger_info_") == true)
                {
                    // Check if we should show NapCat responses
                    bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat WebSocket Response for get_stranger_info: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    string echoId = response["echo"]?.ToString() ?? "";

                    // Process the response and complete the corresponding TaskCompletionSource
                    if (response["status"]?.ToString() == "ok" && response["data"] is JObject data)
                    {
                        // Get the nickname from the response
                        string nickname = data["nickname"]?.ToString() ?? "";

                        if (string.IsNullOrEmpty(nickname))
                        {
                            // If nickname is empty, extract the user ID from the echo ID
                            string userId = echoId.Replace("get_stranger_info_", "").Split('_')[0];
                            nickname = userId; // Fall back to user ID
                            _logger.LogWarning("No nickname found in WebSocket response for echo ID {EchoId}", echoId);
                        }
                        else
                        {
                            _logger.LogInformation("Found nickname in WebSocket response for echo ID {EchoId}: {Nickname}", echoId, nickname);
                        }

                        // Complete the TaskCompletionSource with the nickname
                        lock (_pendingRequests)
                        {
                            if (_pendingRequests.TryGetValue(echoId, out var tcs))
                            {
                                tcs.TrySetResult(nickname);
                                _logger.LogDebug("Completed TaskCompletionSource for echo ID {EchoId} with nickname {Nickname}", echoId, nickname);
                            }
                            else
                            {
                                _logger.LogWarning("No pending request found for echo ID {EchoId}", echoId);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Error in WebSocket response for echo ID {EchoId}: {Status}",
                            echoId, response["status"]?.ToString() ?? "unknown");

                        // Complete the TaskCompletionSource with the user ID (fallback)
                        lock (_pendingRequests)
                        {
                            if (_pendingRequests.TryGetValue(echoId, out var tcs))
                            {
                                string userId = echoId.Replace("get_stranger_info_", "").Split('_')[0];
                                tcs.TrySetResult(userId);
                                _logger.LogDebug("Completed TaskCompletionSource for echo ID {EchoId} with user ID {UserId} (fallback)", echoId, userId);
                            }
                        }
                    }
                }
                // Check if this is a response to get_group_list
                else if (response["data"] != null && response["status"]?.ToString() == "ok")
                {
                    // Check if we should show NapCat responses
                    bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat WebSocket Response: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    // Process group list response
                    ProcessGroupListResponse(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WebSocket message: {Message}", message);
            }

            return;
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

                // 检查是否有图片或文件
                bool hasAttachment = !string.IsNullOrEmpty(message.ImageUrl) || !string.IsNullOrEmpty(message.FileUrl);

                // 如果有附件，尝试使用 NapCat API 发送
                if (hasAttachment)
                {
                    // 先发送文本消息
                    await SendTextMessageAsync(formattedMessage);

                    // 然后发送图片或文件
                    if (!string.IsNullOrEmpty(message.ImageUrl))
                    {
                        await SendImageMessageAsync(message.ImageUrl);
                    }

                    if (!string.IsNullOrEmpty(message.FileUrl))
                    {
                        await SendFileMessageAsync(message.FileUrl, message.FileName);
                    }

                    // 已经发送了消息，直接返回
                    return;
                }

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
        /// 发送纯文本消息到 QQ 群
        /// </summary>
        private async Task SendTextMessageAsync(string text)
        {
            try
            {
                // 检查是否应该显示 NapCat 响应
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                {
                    // 通过 WebSocket 发送
                    await SendWebSocketCommandAsync(new
                    {
                        action = "send_group_msg",
                        @params = new
                        {
                            group_id = _groupId,
                            message = text
                        }
                    });

                    _logger.LogInformation("Text message sent to QQ group {GroupId} via WebSocket", _groupId);
                }
                else
                {
                    // 通过 HTTP 发送
                    var response = await _httpClient.PostJsonAsync<JObject>("/send_group_msg", new
                    {
                        group_id = _groupId,
                        message = text
                    });

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat API Response for text message: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    _logger.LogInformation("Text message sent to QQ group {GroupId} via HTTP", _groupId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send text message to QQ group {GroupId}", _groupId);
            }
        }

        /// <summary>
        /// 发送图片消息到 QQ 群
        /// </summary>
        private async Task SendImageMessageAsync(string imageUrl)
        {
            try
            {
                // 检查是否应该显示 NapCat 响应
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                // 构建图片消息
                var imageMessage = new object[]
                {
                    new
                    {
                        type = "image",
                        data = new
                        {
                            file = imageUrl
                        }
                    }
                };

                if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                {
                    // 通过 WebSocket 发送
                    await SendWebSocketCommandAsync(new
                    {
                        action = "send_group_msg",
                        @params = new
                        {
                            group_id = _groupId,
                            message = imageMessage
                        }
                    });

                    _logger.LogInformation("Image message sent to QQ group {GroupId} via WebSocket: {Url}", _groupId, imageUrl);
                }
                else
                {
                    // 通过 HTTP 发送
                    var response = await _httpClient.PostJsonAsync<JObject>("/send_group_msg", new
                    {
                        group_id = _groupId,
                        message = imageMessage
                    });

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat API Response for image message: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    _logger.LogInformation("Image message sent to QQ group {GroupId} via HTTP: {Url}", _groupId, imageUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send image message to QQ group {GroupId}: {Url}", _groupId, imageUrl);
            }
        }

        /// <summary>
        /// 发送文件消息到 QQ 群
        /// </summary>
        private async Task SendFileMessageAsync(string fileUrl, string fileName)
        {
            try
            {
                // 检查是否应该显示 NapCat 响应
                bool showNapCatResponse = _configuration["Debug:ShowNapCatResponse"]?.ToLower() == "true";

                // 如果没有文件名，从 URL 中提取
                if (string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        fileName = Path.GetFileName(new Uri(fileUrl).LocalPath);
                    }
                    catch
                    {
                        fileName = "file_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    }
                }

                // 构建文件消息
                var fileMessage = new object[]
                {
                    new
                    {
                        type = "file",
                        data = new
                        {
                            file = fileUrl,
                            name = fileName
                        }
                    }
                };

                if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                {
                    // 通过 WebSocket 发送
                    await SendWebSocketCommandAsync(new
                    {
                        action = "send_group_msg",
                        @params = new
                        {
                            group_id = _groupId,
                            message = fileMessage
                        }
                    });

                    _logger.LogInformation("File message sent to QQ group {GroupId} via WebSocket: {FileName}, {Url}",
                        _groupId, fileName, fileUrl);
                }
                else
                {
                    // 通过 HTTP 发送
                    var response = await _httpClient.PostJsonAsync<JObject>("/send_group_msg", new
                    {
                        group_id = _groupId,
                        message = fileMessage
                    });

                    if (showNapCatResponse)
                    {
                        _logger.LogInformation("NapCat API Response for file message: {Response}",
                            response.ToString(Formatting.Indented));
                    }

                    _logger.LogInformation("File message sent to QQ group {GroupId} via HTTP: {FileName}, {Url}",
                        _groupId, fileName, fileUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send file message to QQ group {GroupId}: {FileName}, {Url}",
                    _groupId, fileName, fileUrl);
            }
        }

        /// <summary>
        /// Gets the nickname for a QQ user directly from the API
        /// </summary>
        /// <param name="userId">The QQ user ID</param>
        /// <returns>The user's nickname if found, otherwise the user ID</returns>
        private async Task<string> GetQQUserNicknameAsync(string userId)
        {
            try
            {
                _logger.LogDebug("Getting nickname for QQ user {UserId}", userId);

                try
                {
                    // First try WebSocket if available
                    if (_useWebSocket && _webSocket?.State == WebSocketState.Open)
                    {
                        _logger.LogInformation("Getting nickname for QQ user {UserId} via WebSocket", userId);

                        // Create a unique echo ID for this request
                        string echoId = $"get_stranger_info_{userId}_{DateTime.Now.Ticks}";

                        // Create a TaskCompletionSource to wait for the response
                        var tcs = new TaskCompletionSource<string>();

                        // Create a cancellation token source with a timeout
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                        // Register a callback to handle cancellation
                        cts.Token.Register(() =>
                        {
                            tcs.TrySetResult(userId); // Fall back to user ID on timeout
                            _logger.LogWarning("Timeout waiting for WebSocket response for QQ user {UserId}", userId);
                        }, useSynchronizationContext: false);

                        // Store the TaskCompletionSource in a dictionary to be completed when the response arrives
                        lock (_pendingRequests)
                        {
                            _pendingRequests[echoId] = tcs;
                        }

                        // Send the WebSocket request
                        await SendWebSocketCommandAsync(new
                        {
                            action = "get_stranger_info",
                            echo = echoId,
                            @params = new
                            {
                                user_id = userId,
                                no_cache = true
                            }
                        });

                        _logger.LogDebug("WebSocket request sent for QQ user {UserId} with echo ID {EchoId}", userId, echoId);

                        // Wait for the response or timeout
                        string nickname = await tcs.Task;

                        // Remove the TaskCompletionSource from the dictionary
                        lock (_pendingRequests)
                        {
                            _pendingRequests.Remove(echoId);
                        }

                        if (nickname != userId)
                        {
                            _logger.LogInformation("Found nickname for QQ user {UserId} via WebSocket: {Nickname}", userId, nickname);
                            return nickname;
                        }

                        _logger.LogWarning("Failed to get nickname for QQ user {UserId} via WebSocket, falling back to HTTP", userId);
                    }

                    // Fall back to HTTP if WebSocket is not available or failed
                    _logger.LogInformation("Getting nickname for QQ user {UserId} via HTTP API", userId);

                    var response = await _httpClient.PostJsonAsync<JObject>("/get_stranger_info", new
                    {
                        user_id = userId,
                        no_cache = true
                    });

                    // Always show the API response for debugging
                    _logger.LogInformation("NapCat API User Info Response for user {UserId}: {Response}",
                        userId, response.ToString(Formatting.Indented));

                    if (response["status"]?.ToString() == "ok" && response["data"] is JObject data)
                    {
                        // Get the nickname from the response
                        string nickname = data["nickname"]?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(nickname))
                        {
                            _logger.LogInformation("Found nickname for QQ user {UserId}: {Nickname}", userId, nickname);
                            return nickname;
                        }
                        else
                        {
                            _logger.LogWarning("No nickname found in API response for QQ user {UserId}", userId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("API returned error or invalid data for QQ user {UserId}: {Status}",
                            userId, response["status"]?.ToString() ?? "unknown");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting nickname for QQ user {UserId}: {Message}", userId, ex.Message);
                }

                _logger.LogWarning("Falling back to user ID for QQ user {UserId}", userId);
                return userId; // Fall back to user ID
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting nickname for QQ user {UserId}", userId);
                return userId; // Fall back to user ID
            }
        }

        /// <summary>
        /// Replaces QQ user placeholders with actual nicknames
        /// </summary>
        /// <param name="content">The message content with placeholders</param>
        /// <returns>The content with placeholders replaced by nicknames</returns>
        private async Task<string> ReplaceQQUserPlaceholdersAsync(string content)
        {
            try
            {
                _logger.LogInformation("Checking for QQ user placeholders in content: {Content}", content);

                // Use regex to find all placeholders in the format @__QQ_USER_123456__
                var regex = new System.Text.RegularExpressions.Regex(@"@__QQ_USER_(\d+)__");
                var matches = regex.Matches(content);

                // If no placeholders found, return the original content
                if (matches.Count == 0)
                {
                    _logger.LogInformation("No QQ user placeholders found in content");
                    return content;
                }

                _logger.LogInformation("Found {Count} QQ user placeholders in content", matches.Count);

                // Create a copy of the content that we'll modify
                string result = content;

                // Process each placeholder
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // Extract the QQ ID
                    string qqId = match.Groups[1].Value;
                    _logger.LogInformation("Processing placeholder for QQ user {QQId}", qqId);

                    // Get the nickname for this QQ ID
                    string nickname = await GetQQUserNicknameAsync(qqId);

                    // Replace the placeholder with the nickname
                    string oldText = match.Value;
                    string newText = $"@{nickname}";
                    result = result.Replace(oldText, newText);

                    _logger.LogInformation("Replaced placeholder '{OldText}' with '{NewText}' for QQ user {QQId}",
                        oldText, newText, qqId);
                }

                _logger.LogInformation("Final content after replacing placeholders: {Content}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replacing QQ user placeholders");
                return content; // Return the original content in case of error
            }
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
                                    // 处理图片
                                    if (part["data"]?["url"] != null)
                                    {
                                        string imageUrl = part["data"]?["url"]?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(imageUrl))
                                        {
                                            // 存储图片 URL 以便后续处理
                                            _lastImageUrl = imageUrl;
                                            _logger.LogDebug("Found image URL in QQ message: {Url}", imageUrl);
                                        }
                                    }
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

                                        // Always use a placeholder for @ mentions to ensure we get the most up-to-date nickname
                                        string placeholder = $"@__QQ_USER_{qqId}__";
                                        contentBuilder.Append(placeholder);
                                        _logger.LogInformation("Added placeholder for QQ user {QQId}: {Placeholder}", qqId, placeholder);

                                        // Add a space after the mention
                                        contentBuilder.Append(" ");
                                    }
                                    break;

                                case "file":
                                    // 处理文件
                                    if (part["data"] != null)
                                    {
                                        string? fileUrl = part["data"]?["url"]?.ToString();
                                        string? fileName = part["data"]?["name"]?.ToString();

                                        if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(fileName))
                                        {
                                            // 存储文件信息以便后续处理
                                            _lastFileUrl = fileUrl;
                                            _lastFileName = fileName;
                                            _lastFileType = "document"; // 默认为文档类型

                                            // 尝试根据文件扩展名确定类型
                                            string extension = Path.GetExtension(fileName).ToLowerInvariant();
                                            if (extension.StartsWith("."))
                                            {
                                                extension = extension.Substring(1);
                                            }

                                            // 根据扩展名确定文件类型
                                            if (new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp" }.Contains(extension))
                                            {
                                                _lastFileType = "image";
                                            }
                                            else if (new[] { "mp4", "avi", "mov", "wmv", "flv", "mkv" }.Contains(extension))
                                            {
                                                _lastFileType = "video";
                                            }
                                            else if (new[] { "mp3", "wav", "ogg", "flac", "aac", "m4a" }.Contains(extension))
                                            {
                                                _lastFileType = "audio";
                                            }

                                            _logger.LogDebug("Found file in QQ message: {FileName}, Type: {FileType}, URL: {FileUrl}",
                                                _lastFileName, _lastFileType, _lastFileUrl);
                                        }
                                    }
                                    contentBuilder.Append("[File]");
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

                                // Replace @ placeholders with actual nicknames
                                messageContent = await ReplaceQQUserPlaceholdersAsync(messageContent);

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
