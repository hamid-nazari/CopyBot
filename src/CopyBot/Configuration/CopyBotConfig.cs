using System.Text.Json.Serialization;

namespace CopyBot.Configuration;

/// <summary>
/// Strongly-typed representation of the <c>config.json</c> settings.
/// Every member has a sensible default so the service can run with no config at all.
/// </summary>
public sealed class CopyBotConfig
{
    /// <summary>Root folder on the PC where removable drive contents are copied.</summary>
    public string BackupRootFolder { get; set; } = @"C:\Backups";

    /// <summary>Seconds to wait after a removable drive is detected before copying starts.</summary>
    public double CopyDelaySeconds { get; set; } = 10.0;

    /// <summary>Drive types to watch (e.g. "Removable", "Fixed", "CDRom").</summary>
    public List<string> SourceDriveTypes { get; set; } = new() { "Removable" };

    public LogConfig Log { get; set; } = new();

    public CopyConfig Copy { get; set; } = new();

    /// <summary>Runtime-only message describing why defaults were used (never serialized).</summary>
    [JsonIgnore]
    public string? LoadWarning { get; set; }

    public sealed class LogConfig
    {
        /// <summary>Directory that holds the rolling log file.</summary>
        public string Directory { get; set; } = @"C:\ProgramData\CopyBot\Logs";

        /// <summary>Log file name pattern; <c>{date}</c> is replaced with yyyy-MM-dd.</summary>
        public string FileNamePattern { get; set; } = "copybot-{date}.log";

        /// <summary>Minimum log level: Debug, Info, Warning, Error.</summary>
        public string Level { get; set; } = "Info";

        /// <summary>Also write entries to the Windows Event Log.</summary>
        public bool WriteToEventLog { get; set; } = true;

        public string EventLogSource { get; set; } = "Windows Shadow Sync Service";

        public string EventLogName { get; set; } = "Application";

        /// <summary>Log files older than this many days are deleted on startup.</summary>
        public int MaxRetentionDays { get; set; } = 30;
    }

    public sealed class CopyConfig
    {
        /// <summary>Preserve the source last-modified timestamp on copied files.</summary>
        public bool PreserveTimestamps { get; set; } = true;

        /// <summary>Seconds of tolerance when comparing last-modified timestamps.</summary>
        public double TimestampToleranceSeconds { get; set; } = 2.0;

        /// <summary>Template used to build each backup sub-folder name.</summary>
        public string SubfolderNameFormat { get; set; } = "{name}_{date}";

        /// <summary>Copy buffer size in kilobytes.</summary>
        public int BufferSizeKilobytes { get; set; } = 1024;

        /// <summary>Attempts before a single file copy is reported as failed.</summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>Do not descend into reparse points (junctions/symlinks).</summary>
        public bool SkipReparsePoints { get; set; } = true;

        /// <summary>Directory names that are never copied.</summary>
        public List<string> ExcludedDirectoryNames { get; set; } = new()
        {
            "System Volume Information",
            "$Recycle.Bin",
            "$RECYCLE.BIN",
            "Recovery",
            "$SysReset",
            "$Windows.~BT",
            "$Windows.~WS"
        };

        /// <summary>
        /// Glob patterns for file paths to include. When non-empty, only files whose
        /// path matches one of these patterns are copied; all others are skipped.
        /// Default: empty (include everything).
        /// </summary>
        public List<string> Included { get; set; } = new();

        /// <summary>
        /// Glob patterns for file paths to exclude. When a path matches any pattern it
        /// is skipped, and this takes precedence over <see cref="Included"/>.
        /// Default: empty (exclude nothing).
        /// </summary>
        public List<string> Excluded { get; set; } = new();

        [JsonIgnore]
        public int BufferSizeBytes => Math.Max(4096, Math.Max(1, BufferSizeKilobytes) * 1024);
    }
}