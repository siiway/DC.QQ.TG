using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DC.QQ.TG.Interfaces;
using DC.QQ.TG.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DC.QQ.TG.Services
{
    public class MessageService : BackgroundService
    {
        private readonly IEnumerable<IMessageAdapter> _adapters;
        private readonly ILogger<MessageService> _logger;
        private readonly Dictionary<string, HashSet<string>> _processedMessages = new();



        public MessageService(IEnumerable<IMessageAdapter> adapters, ILogger<MessageService> logger)
        {
            _adapters = adapters;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Initializing message adapters...");

                // Initialize all adapters
                foreach (var adapter in _adapters)
                {
                    try
                    {
                        await adapter.InitializeAsync();
                        adapter.MessageReceived += OnMessageReceived;
                        _processedMessages[adapter.Platform.ToString()] = new HashSet<string>();
                        _logger.LogInformation("Successfully initialized {Platform} adapter", adapter.Platform);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialize {Platform} adapter", adapter.Platform);
                    }
                }

                _logger.LogInformation("Starting message adapters...");

                // Start listening on all adapters
                foreach (var adapter in _adapters)
                {
                    try
                    {
                        await adapter.StartListeningAsync();
                        _logger.LogInformation("Started listening on {Platform} adapter", adapter.Platform);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start listening on {Platform} adapter", adapter.Platform);
                    }
                }

                _logger.LogInformation("Message service started");

                // Keep the service running until cancellation is requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in message service");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping message service...");

            // Stop all adapters
            foreach (var adapter in _adapters)
            {
                adapter.MessageReceived -= OnMessageReceived;
                await adapter.StopListeningAsync();
            }

            await base.StopAsync(cancellationToken);
        }

        private async void OnMessageReceived(object sender, Message message)
        {
            try
            {
                // Check if we've already processed this message
                if (IsMessageProcessed(message))
                {
                    return;
                }

                // Mark the message as processed
                MarkMessageAsProcessed(message);

                _logger.LogInformation("Received message from {Source}: {Message}",
                    message.Source, message.Content);

                // Forward the message to all other platforms
                var sourceAdapter = sender as IMessageAdapter;

                // Forward the message to all other platforms except the source
                if (sourceAdapter != null)
                {
                    var targetAdapters = _adapters.Where(a => a.Platform != sourceAdapter.Platform);

                    foreach (var adapter in targetAdapters)
                    {
                        await adapter.SendMessageAsync(message);
                    }
                }

                // Print the message to the console
                Console.WriteLine($"[{message.Source}] {message.SenderName}: {message.Content}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        }

        private bool IsMessageProcessed(Message message)
        {
            return _processedMessages[message.Source.ToString()].Contains(message.Id);
        }

        private void MarkMessageAsProcessed(Message message)
        {
            _processedMessages[message.Source.ToString()].Add(message.Id);

            // Limit the size of the processed messages set
            if (_processedMessages[message.Source.ToString()].Count > 1000)
            {
                _processedMessages[message.Source.ToString()] =
                    _processedMessages[message.Source.ToString()].Skip(500).ToHashSet();
            }
        }


    }
}
