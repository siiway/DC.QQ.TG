using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DC.QQ.TG.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DC.QQ.TG.Services
{
    public class DebugShellService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DebugShellService> _logger;
        private readonly IEnumerable<IMessageAdapter> _adapters;
        private readonly MessageService _messageService;
        private bool _isEnabled;

        // Dictionary to store shell variables
        private readonly Dictionary<string, string> _shellVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // List to store recent messages
        private readonly List<Models.Message> _recentMessages = new List<Models.Message>();
        private const int MaxStoredMessages = 100;

        public DebugShellService(
            IConfiguration configuration,
            ILogger<DebugShellService> logger,
            IEnumerable<IMessageAdapter> adapters,
            MessageService messageService)
        {
            _configuration = configuration;
            _logger = logger;
            _adapters = adapters;
            _messageService = messageService;
            _isEnabled = _configuration["Debug:EnableShell"]?.ToLower() == "true";

            // Initialize shell variables with configuration values
            InitializeShellVariables();

            // Subscribe to message events from all adapters
            foreach (var adapter in _adapters)
            {
                adapter.MessageReceived += OnMessageReceived;
            }
        }

        private void InitializeShellVariables()
        {
            // Add configuration values to shell variables
            _shellVariables["discord_webhook"] = _configuration["Discord:WebhookUrl"] ?? string.Empty;
            _shellVariables["telegram_token"] = _configuration["Telegram:BotToken"] ?? string.Empty;
            _shellVariables["telegram_chat"] = _configuration["Telegram:ChatId"] ?? string.Empty;
            _shellVariables["napcat_url"] = _configuration["NapCat:BaseUrl"] ?? string.Empty;
            _shellVariables["napcat_token"] = _configuration["NapCat:Token"] ?? string.Empty;
            _shellVariables["qq_group"] = _configuration["NapCat:GroupId"] ?? string.Empty;
            _shellVariables["disable_telegram"] = _configuration["Disabled:Telegram"] ?? "false";
            _shellVariables["disable_discord"] = _configuration["Disabled:Discord"] ?? "false";
            _shellVariables["disable_qq"] = _configuration["Disabled:QQ"] ?? "false";
            _shellVariables["show_napcat_response"] = _configuration["Debug:ShowNapCatResponse"] ?? "false";
            _shellVariables["debug_shell"] = _configuration["Debug:EnableShell"] ?? "false";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("Debug shell is disabled");
                return;
            }

            _logger.LogInformation("Debug shell is enabled. Type 'help' for available commands.");

            // Start a background task to process log entries
            var logProcessingTask = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Check for pending logs
                    while (Utils.DebugShellLogger.HasPendingLogs)
                    {
                        var logEntry = Utils.DebugShellLogger.DequeueLogEntry();
                        if (logEntry != null)
                        {
                            // Format the log entry as a system-writeline command
                            string formattedLog = logEntry.GetFormattedMessage();

                            // Escape any markup in the log message
                            formattedLog = formattedLog.Replace("[", "[[").Replace("]", "]]");

                            // Display the log entry
                            lock (Console.Out)
                            {
                                // Clear current line
                                Console.Write("\r\u001b[K");

                                // Parse the log entry to maintain its original color
                                string timestamp = logEntry.Timestamp.ToString("HH:mm:ss.fff");
                                string level = GetLogLevelColor(logEntry.LogLevel);
                                string category = logEntry.CategoryName.Split('.').LastOrDefault() ?? logEntry.CategoryName;

                                // Format: HH:mm:ss.fff LEVEL Category Message
                                AnsiConsole.MarkupLine($"{timestamp} {level} {category} {logEntry.Message}");

                                // Restore the prompt
                                AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            }
                        }
                    }

                    // Wait a short time before checking again
                    await Task.Delay(100, stoppingToken);
                }
            }, stoppingToken);

            // Display the initial prompt
            AnsiConsole.Markup("[blue]debug-shell>[/] ");

            await Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    string command = Console.ReadLine()?.Trim().ToLower() ?? string.Empty;

                    // We'll display the prompt after processing the command

                    if (string.IsNullOrEmpty(command))
                        continue;

                    // Ignore system-writeline commands (these are handled by the log processor)
                    if (command.StartsWith("system-writeline"))
                    {
                        continue;
                    }

                    // Check for exit command first
                    if (command == "exit" || command == "quit")
                    {
                        _logger.LogInformation("Exiting application");
                        // Exit the application completely
                        Environment.Exit(0);
                        return;
                    }

                    switch (command)
                    {
                        case "help":
                            ShowHelp();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "status":
                            ShowStatus();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "adapters":
                            ListAdapters();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "send":
                            await SendTestMessage();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "vars":
                        case "variables":
                            ShowVariables();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "get":
                            ShowVariables();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "messages":
                        case "msgs":
                            await GetRecentMessages();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        case "clear":
                        case "cls":
                            Console.Clear();
                            AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            break;
                        default:
                            if (command.StartsWith("send "))
                            {
                                string message = command.Substring(5).Trim();
                                await SendCustomMessage(message);
                                AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            }
                            else if (command.StartsWith("get "))
                            {
                                string getCommand = command.Substring(4).Trim();

                                if (getCommand.StartsWith("messages ") || getCommand.StartsWith("msgs "))
                                {
                                    string countStr = getCommand.Contains("messages ")
                                        ? getCommand.Substring(9).Trim()
                                        : getCommand.Substring(5).Trim();

                                    if (int.TryParse(countStr, out int count))
                                    {
                                        await GetRecentMessages(count);
                                    }
                                    else
                                    {
                                        AnsiConsole.MarkupLine("[yellow]Invalid count. Usage: get messages <count>[/]");
                                    }
                                }
                                else
                                {
                                    GetVariable(getCommand);
                                }
                                AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            }
                            else if (command.StartsWith("set "))
                            {
                                string setCommand = command.Substring(4).Trim();
                                SetVariable(setCommand);
                                AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine("[red]Unknown command. Type 'help' for available commands.[/]");
                                AnsiConsole.Markup("[blue]debug-shell>[/] ");
                            }
                            break;
                    }
                }
            }, stoppingToken);
        }

        private void OnMessageReceived(object sender, Models.Message message)
        {
            // Add the message to our recent messages list
            lock (_recentMessages)
            {
                _recentMessages.Add(message);

                // Keep only the most recent messages
                if (_recentMessages.Count > MaxStoredMessages)
                {
                    _recentMessages.RemoveAt(0);
                }
            }
        }

        private async Task GetRecentMessages(int count = 10)
        {
            if (count <= 0)
            {
                count = 10;
            }

            if (count > MaxStoredMessages)
            {
                count = MaxStoredMessages;
            }

            List<Models.Message> messages;
            lock (_recentMessages)
            {
                // Get the most recent messages
                messages = _recentMessages
                    .OrderByDescending(m => m.Timestamp)
                    .Take(count)
                    .ToList();
            }

            if (messages.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No messages received yet.[/]");
                return;
            }

            // Display the messages
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Time").LeftAligned())
                .AddColumn(new TableColumn("Source").LeftAligned())
                .AddColumn(new TableColumn("Sender").LeftAligned())
                .AddColumn(new TableColumn("Content").LeftAligned());

            foreach (var message in messages.OrderBy(m => m.Timestamp))
            {
                string timeStr = message.Timestamp.ToString("HH:mm:ss");
                string sourceStr = message.Source.ToString();
                string senderStr = message.SenderName;
                string contentStr = message.Content.Length > 50
                    ? message.Content.Substring(0, 47) + "..."
                    : message.Content;

                // Escape any markup in the content
                contentStr = contentStr.Replace("[", "[[").Replace("]", "]]");
                senderStr = senderStr.Replace("[", "[[").Replace("]", "]]");

                table.AddRow(
                    timeStr,
                    $"[cyan]{sourceStr}[/]",
                    senderStr,
                    contentStr
                );
            }

            AnsiConsole.Write(table);
        }

        private string GetLogLevelColor(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "[grey]TRACE[/]",
                LogLevel.Debug => "[grey]DEBUG[/]",
                LogLevel.Information => "[green]INFO [/]",
                LogLevel.Warning => "[yellow]WARN [/]",
                LogLevel.Error => "[red]ERROR[/]",
                LogLevel.Critical => "[red]CRIT [/]",
                _ => "[grey]     [/]"
            };
        }

        private void ShowHelp()
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Command").LeftAligned())
                .AddColumn(new TableColumn("Description").LeftAligned());

            table.AddRow("help", "Show this help message");
            table.AddRow("exit, quit", "Exit the application");
            table.AddRow("status", "Show current status");
            table.AddRow("adapters", "List all registered adapters");
            table.AddRow("send", "Send a test message to all platforms");
            table.AddRow("send <message>", "Send a custom message to all platforms");
            table.AddRow("vars, variables, get", "Show all variables");
            table.AddRow("get <name>", "Show the value of a specific variable");
            table.AddRow("set <name> <value>", "Set the value of a variable");
            table.AddRow("messages, msgs", "Show the 10 most recent messages");
            table.AddRow("get messages <count>", "Show the specified number of recent messages");
            table.AddRow("clear, cls", "Clear the console screen");

            AnsiConsole.Write(table);
        }

        private void ShowVariables()
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Variable").LeftAligned())
                .AddColumn(new TableColumn("Value").LeftAligned());

            foreach (var variable in _shellVariables.OrderBy(v => v.Key))
            {
                string value = variable.Value;

                // Mask sensitive values
                if (variable.Key.Contains("token") || variable.Key.Contains("webhook"))
                {
                    if (!string.IsNullOrEmpty(value) && value.Length > 8)
                    {
                        value = value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
                    }
                }

                table.AddRow(variable.Key, value);
            }

            AnsiConsole.Write(table);
        }

        private void GetVariable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                AnsiConsole.MarkupLine("[yellow]Please specify a variable name.[/]");
                return;
            }

            if (_shellVariables.TryGetValue(name, out string value))
            {
                // Mask sensitive values
                if (name.Contains("token") || name.Contains("webhook"))
                {
                    if (!string.IsNullOrEmpty(value) && value.Length > 8)
                    {
                        string maskedValue = value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
                        AnsiConsole.MarkupLine($"[cyan]{name}[/] = [green]{maskedValue}[/] (masked for security)");

                        // Ask if user wants to see the full value
                        if (AnsiConsole.Confirm("Show full value?", false))
                        {
                            AnsiConsole.MarkupLine($"[cyan]{name}[/] = [green]{value}[/]");
                        }
                        return;
                    }
                }

                AnsiConsole.MarkupLine($"[cyan]{name}[/] = [green]{value}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Variable '{name}' not found.[/]");
            }
        }

        private void SetVariable(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                AnsiConsole.MarkupLine("[yellow]Usage: set <name> <value>[/]");
                return;
            }

            // Split the command into name and value
            int spaceIndex = command.IndexOf(' ');
            if (spaceIndex <= 0)
            {
                AnsiConsole.MarkupLine("[yellow]Usage: set <name> <value>[/]");
                return;
            }

            string name = command.Substring(0, spaceIndex).Trim();
            string value = command.Substring(spaceIndex + 1).Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
            {
                AnsiConsole.MarkupLine("[yellow]Usage: set <name> <value>[/]");
                return;
            }

            // Update the variable
            _shellVariables[name] = value;
            AnsiConsole.MarkupLine($"[green]Variable '{name}' set to '{value}'.[/]");

            // Update configuration if it's a known variable
            UpdateConfiguration(name, value);
        }

        private void UpdateConfiguration(string name, string value)
        {
            // Map shell variable names to configuration keys
            string configKey = name switch
            {
                "discord_webhook" => "Discord:WebhookUrl",
                "telegram_token" => "Telegram:BotToken",
                "telegram_chat" => "Telegram:ChatId",
                "napcat_url" => "NapCat:BaseUrl",
                "napcat_token" => "NapCat:Token",
                "qq_group" => "NapCat:GroupId",
                "disable_telegram" => "Disabled:Telegram",
                "disable_discord" => "Disabled:Discord",
                "disable_qq" => "Disabled:QQ",
                "show_napcat_response" => "Debug:ShowNapCatResponse",
                "debug_shell" => "Debug:EnableShell",
                _ => null
            };

            if (configKey != null)
            {
                // We can't directly modify IConfiguration, but we can log that it would be updated
                AnsiConsole.MarkupLine($"[yellow]Note: Configuration '{configKey}' would be updated to '{value}' on restart.[/]");
            }
        }

        private void ShowStatus()
        {
            bool discordDisabled = _configuration["Disabled:Discord"]?.ToLower() == "true";
            bool telegramDisabled = _configuration["Disabled:Telegram"]?.ToLower() == "true";
            bool qqDisabled = _configuration["Disabled:QQ"]?.ToLower() == "true";

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Platform").LeftAligned())
                .AddColumn(new TableColumn("Status").LeftAligned());

            table.AddRow("Discord", discordDisabled ? "[dim]Disabled[/]" : "[green]Enabled[/]");
            table.AddRow("Telegram", telegramDisabled ? "[dim]Disabled[/]" : "[green]Enabled[/]");
            table.AddRow("QQ", qqDisabled ? "[dim]Disabled[/]" : "[green]Enabled[/]");

            AnsiConsole.Write(table);
        }

        private void ListAdapters()
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Type").LeftAligned())
                .AddColumn(new TableColumn("Status").LeftAligned());

            foreach (var adapter in _adapters)
            {
                // We don't have a direct way to check if an adapter is listening,
                // so we'll just show the adapter type and platform
                table.AddRow(
                    adapter.GetType().Name,
                    $"[cyan]{adapter.Platform}[/]"
                );
            }

            AnsiConsole.Write(table);
        }

        private async Task SendTestMessage()
        {
            string testMessage = "This is a test message from the debug shell.";
            await SendCustomMessage(testMessage);
        }

        private async Task SendCustomMessage(string content)
        {
            try
            {
                var message = new Models.Message
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = content,
                    SenderName = "DebugShell",
                    SenderId = "system",
                    Source = Models.MessageSource.System,
                    Timestamp = DateTime.Now
                };

                // Add the message to our recent messages list
                lock (_recentMessages)
                {
                    _recentMessages.Add(message);

                    // Keep only the most recent messages
                    if (_recentMessages.Count > MaxStoredMessages)
                    {
                        _recentMessages.RemoveAt(0);
                    }
                }

                // Send the message to all adapters
                foreach (var adapter in _adapters)
                {
                    await adapter.SendMessageAsync(message);
                }
                AnsiConsole.MarkupLine("[green]Message sent successfully.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to send message: {ex.Message.Replace("[", "[[").Replace("]", "]]")}[/]");
            }
        }
    }
}
