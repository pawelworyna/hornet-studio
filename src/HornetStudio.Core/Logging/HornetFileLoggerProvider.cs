using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HornetStudio.Core.Logging;

public sealed class HornetFileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly FileLoggerOptions _options;
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly ConcurrentDictionary<string, HornetFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public HornetFileLoggerProvider(FileLoggerOptions? options = null)
    {
        _options = options ?? new FileLoggerOptions();
        Directory.CreateDirectory(_options.LogDirectory);

        // Bounded channel to prevent memory overflow in extreme cases, dropping oldest if full
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        _writerTask = Task.Run(ProcessLogQueueAsync);

        // Clean up old log files
        _ = Task.Run(CleanupOldLogs);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new HornetFileLogger(name, this, _options));
    }

    internal void EnqueueLog(string logMessage)
    {
        _channel.Writer.TryWrite(logMessage);
    }

    private async Task ProcessLogQueueAsync()
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var currentFilePath = Path.Combine(_options.LogDirectory, $"{_options.FilePrefix}{today}.log");

                using var stream = new FileStream(currentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);

                while (reader.TryRead(out var message))
                {
                    await writer.WriteLineAsync(message);
                }

                await writer.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Drain remaining items on cancellation
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var currentFilePath = Path.Combine(_options.LogDirectory, $"{_options.FilePrefix}{today}.log");
                using var stream = new FileStream(currentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);

                while (reader.TryRead(out var msg))
                {
                    await writer.WriteLineAsync(msg);
                }
                await writer.FlushAsync();
            }
            catch { }
        }
        catch
        {
            // Do not crash the application due to logging errors
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_options.RetainedDays);
            var dirInfo = new DirectoryInfo(_options.LogDirectory);

            foreach (var file in dirInfo.GetFiles($"{_options.FilePrefix}*.log"))
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    try { file.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore retention cleanup errors
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try
        {
            if (!_writerTask.Wait(TimeSpan.FromSeconds(3)))
            {
                _cts.Cancel();
            }
        }
        catch { }
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            _cts.Cancel();
        }
        catch { }
        _cts.Dispose();
    }
}

public sealed class HornetFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly HornetFileLoggerProvider _provider;
    private readonly FileLoggerOptions _options;

    public HornetFileLogger(string categoryName, HornetFileLoggerProvider provider, FileLoggerOptions options)
    {
        _categoryName = categoryName;
        _provider = provider;
        _options = options;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinLogLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        // Sanitize sensitive credentials (passwords, tokens)
        message = SensitiveDataSanitizer.Sanitize(message);

        var levelTag = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "LOG"
        };

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var shortCategory = GetShortCategory(_categoryName);

        var logBuilder = new StringBuilder();
        logBuilder.Append($"[{timestamp}] [{levelTag}] [{shortCategory}] {message}");

        if (exception != null)
        {
            logBuilder.AppendLine();
            logBuilder.Append($"Exception: {exception.GetType().FullName}: {SensitiveDataSanitizer.Sanitize(exception.Message)}");
            logBuilder.AppendLine();
            logBuilder.Append(exception.StackTrace);
        }

        _provider.EnqueueLog(logBuilder.ToString());
    }

    private static string GetShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1
            ? category[(lastDot + 1)..]
            : category;
    }
}
