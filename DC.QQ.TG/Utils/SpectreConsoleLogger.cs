using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DC.QQ.TG.Utils
{
    public class SpectreConsoleLoggerProvider : ILoggerProvider
    {
        private readonly IDisposable _onChangeToken;
        private SpectreConsoleLoggerConfiguration _currentConfig;
        private readonly ConcurrentDictionary<string, SpectreConsoleLogger> _loggers = new();

        public SpectreConsoleLoggerProvider(IOptionsMonitor<SpectreConsoleLoggerConfiguration> config)
        {
            _currentConfig = config.CurrentValue;
            _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new SpectreConsoleLogger(name, GetCurrentConfig));
        }

        private SpectreConsoleLoggerConfiguration GetCurrentConfig() => _currentConfig;

        public void Dispose()
        {
            _loggers.Clear();
            _onChangeToken.Dispose();
        }
    }

    public class SpectreConsoleLogger : ILogger
    {
        private readonly string _name;
        private readonly Func<SpectreConsoleLoggerConfiguration> _getCurrentConfig;

        public SpectreConsoleLogger(string name, Func<SpectreConsoleLoggerConfiguration> getCurrentConfig)
        {
            _name = name;
            _getCurrentConfig = getCurrentConfig;
        }

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel)
        {
            return _getCurrentConfig().LogLevels.ContainsKey(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var config = _getCurrentConfig();
            if (!config.LogLevels.TryGetValue(logLevel, out var color))
            {
                return;
            }

            // Get category name without namespace
            string category = _name;
            int lastDot = _name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                category = _name.Substring(lastDot + 1);
            }

            // Format timestamp
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            // Create a table for the log entry
            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn(""))
                .AddColumn(new TableColumn("").Padding(0, 0));

            // Create the log level marker
            var levelText = GetLogLevelString(logLevel);
            var levelMarkup = new Markup($"[{color}]{levelText}[/]");

            // Create the log message and escape any markup to prevent parsing errors
            string message = formatter(state, exception);
            string escapedMessage = message.Replace("[", "[[").Replace("]", "]]");
            var messageMarkup = new Markup(escapedMessage);

            // Add the row with timestamp, level, category, and message
            table.AddRow(
                new Markup($"[grey]{timestamp}[/] [{color}]{levelText}[/] [cyan]{category}[/]"),
                messageMarkup
            );

            // Render the table
            AnsiConsole.Write(table);

            // Print exception if exists
            if (exception != null)
            {
                // Escape any markup in the exception message to prevent parsing errors
                string escapedExceptionText = exception.ToString().Replace("[", "[[").Replace("]", "]]");

                var exceptionPanel = new Panel(escapedExceptionText)
                    .Header("Exception Details")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Red);

                AnsiConsole.Write(exceptionPanel);
            }
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => "NONE "
            };
        }
    }

    public class SpectreConsoleLoggerConfiguration
    {
        public int EventId { get; set; }

        public Dictionary<LogLevel, string> LogLevels { get; set; } = new()
        {
            [LogLevel.Information] = "green",
            [LogLevel.Warning] = "yellow",
            [LogLevel.Error] = "red",
            [LogLevel.Critical] = "red bold",
            [LogLevel.Debug] = "grey",
            [LogLevel.Trace] = "grey dim"
        };
    }

    public static class SpectreConsoleLoggerExtensions
    {
        public static ILoggingBuilder AddSpectreConsoleLogger(this ILoggingBuilder builder)
        {
            builder.AddConfiguration();

            builder.Services.AddSingleton<ILoggerProvider, SpectreConsoleLoggerProvider>();
            builder.Services.Configure<SpectreConsoleLoggerConfiguration>(c => { });

            LoggerProviderOptions.RegisterProviderOptions<SpectreConsoleLoggerConfiguration, SpectreConsoleLoggerProvider>(builder.Services);

            return builder;
        }
    }
}
