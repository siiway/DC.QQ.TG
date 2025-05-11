using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DC.QQ.TG.Utils
{
    public class DebugShellLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Func<bool> _isEnabled;
        private static readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private static readonly object _lock = new object();

        public DebugShellLogger(string categoryName, Func<bool> isEnabled)
        {
            _categoryName = categoryName;
            _isEnabled = isEnabled;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _isEnabled();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            string message = formatter(state, exception);

            if (string.IsNullOrEmpty(message) && exception == null)
                return;

            // Create a log entry
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                LogLevel = logLevel,
                CategoryName = _categoryName,
                Message = message,
                Exception = exception
            };

            // Add to queue
            _logQueue.Enqueue(entry);
        }

        public static LogEntry DequeueLogEntry()
        {
            if (_logQueue.TryDequeue(out LogEntry entry))
                return entry;

            return null;
        }

        public static bool HasPendingLogs => !_logQueue.IsEmpty;

        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            private NullScope() { }

            public void Dispose() { }
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel LogLevel { get; set; }
        public string CategoryName { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public string GetFormattedMessage()
        {
            string timestamp = Timestamp.ToString("HH:mm:ss.fff");
            string level = GetLogLevelString(LogLevel);
            string category = CategoryName.Split('.').LastOrDefault() ?? CategoryName;

            // Format: HH:mm:ss.fff LEVEL Category Message
            return $"{timestamp} {level} {category} {Message}";
        }

        private string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => "     "
            };
        }
    }

    public class DebugShellLoggerProvider : ILoggerProvider
    {
        private readonly Func<bool> _isEnabled;

        public DebugShellLoggerProvider(Func<bool> isEnabled)
        {
            _isEnabled = isEnabled;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DebugShellLogger(categoryName, _isEnabled);
        }

        public void Dispose() { }
    }

    public static class DebugShellLoggerExtensions
    {
        public static ILoggingBuilder AddDebugShellLogger(this ILoggingBuilder builder, Func<bool> isEnabled)
        {
            builder.AddProvider(new DebugShellLoggerProvider(isEnabled));
            return builder;
        }
    }
}
