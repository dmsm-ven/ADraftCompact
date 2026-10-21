using System.IO;
using Microsoft.Extensions.Logging;
using ADraftCompact.Services;

namespace ADraftCompact.Logging;

/// <summary>
/// Writes application logs to one file (<see cref="AppPaths.LogFile"/>, next to the executable), appended across runs.
/// The file is started afresh when it grows beyond <see cref="MaxBytes"/>, so it can never grow without limit.
/// If the file cannot be opened (e.g. the app sits in a read-only folder) logging is silently switched off instead of crashing the app.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly object _sync = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public string LogFilePath { get; } = AppPaths.LogFile;

    public FileLoggerProvider()
    {
        try
        {
            var mode = File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxBytes ? FileMode.Create : FileMode.Append;
            _writer = new StreamWriter(new FileStream(LogFilePath, mode, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [Information] [Logging] ---- application started ----");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, WriteLine);

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            if (_disposed || _writer is null) return;
            try { _writer.WriteLine(line); }
            catch (IOException) { /* a full disk must not take the application down */ }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer?.Dispose();
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
