using System.Diagnostics;
using System.Text;
using CopyBot.Configuration;

namespace CopyBot.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Thread-safe logger that writes to a rolling file (configurable path) and,
/// optionally, to the Windows Event Log. Logging never throws into the caller.
/// </summary>
public sealed class ActivityLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly CopyBotConfig.LogConfig _config;
    private readonly LogLevel _minimumLevel;
    private readonly bool _fileLoggingEnabled;
    private EventLog? _eventLog;
    private bool _eventLogReady;

    public ActivityLogger(CopyBotConfig.LogConfig config)
    {
        _config = config;
        _minimumLevel = ParseLevel(config.Level);
        _fileLoggingEnabled = !string.IsNullOrWhiteSpace(config.Directory);

        if (config.WriteToEventLog)
            TryInitEventLog();

        if (_fileLoggingEnabled)
            CleanupOldLogFiles();
    }

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warning, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Debug(string message) => Write(LogLevel.Debug, message);

    private void TryInitEventLog()
    {
        try
        {
            var source = _config.EventLogSource;
            var logName = string.IsNullOrWhiteSpace(_config.EventLogName) ? "Application" : _config.EventLogName;

            if (!EventLog.SourceExists(source))
                EventLog.CreateEventSource(source, logName);

            _eventLog = new EventLog(logName, ".", source);
            _eventLogReady = true;
        }
        catch (Exception ex)
        {
            // Event log source creation requires elevation; a service normally has it,
            // but a console debug run may not. Log the inability to a file instead.
            _eventLogReady = false;
            SafeFileWrite($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Warn] " +
                          $"Event Log source could not be configured: {ex.Message}");
        }
    }

    private void CleanupOldLogFiles()
    {
        if (_config.MaxRetentionDays <= 0)
            return;

        try
        {
            var dir = _config.Directory;
            if (!Directory.Exists(dir))
                return;

            var cutoff = DateTime.Now.AddDays(-_config.MaxRetentionDays);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best effort; never let cleanup break logging.
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private void Write(LogLevel level, string message)
    {
        if (level < _minimumLevel)
            return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        lock (_sync)
        {
            SafeFileWrite(line);
            WriteEventLog(level, message);
        }
    }

    private void WriteEventLog(LogLevel level, string message)
    {
        if (!_eventLogReady || _eventLog is null)
            return;

        var entryType = level switch
        {
            LogLevel.Error => EventLogEntryType.Error,
            LogLevel.Warning => EventLogEntryType.Warning,
            LogLevel.Debug => EventLogEntryType.Information,
            _ => EventLogEntryType.Information
        };

        try
        {
            _eventLog.WriteEntry(message, entryType);
        }
        catch
        {
            // Best effort.
        }
    }

    private void SafeFileWrite(string line)
    {
        if (!_fileLoggingEnabled)
            return;

        try
        {
            Directory.CreateDirectory(_config.Directory);
            var path = GetLogFilePath(DateTime.Now);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must never take the service down.
        }
    }

    private string GetLogFilePath(DateTime date)
    {
        var name = _config.FileNamePattern
            .Replace("{date}", date.ToString("yyyy-MM-dd"))
            .Replace("{fulldate}", date.ToString("yyyyMMdd"));

        return Path.Combine(_config.Directory, name);
    }

    private static LogLevel ParseLevel(string? level)
    {
        return Enum.TryParse(level, ignoreCase: true, out LogLevel parsed)
            ? parsed
            : LogLevel.Info;
    }

    public void Dispose()
    {
        _eventLog?.Dispose();
        _eventLog = null;
    }
}