using System.IO;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Logging;

/// <summary>Writes application logs to a rolling-by-startup text file in LocalAppData.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string LogFilePath { get; }

    public FileLoggerProvider()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbilityDraftOverlay", "Logs");
        Directory.CreateDirectory(logDirectory);
        LogFilePath = Path.Combine(logDirectory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, WriteLine);

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            if (!_disposed)
                _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(string category, Action<string> writeLine) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message += Environment.NewLine + exception;

            writeLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] [{category}] {message}");
        }
    }
}
