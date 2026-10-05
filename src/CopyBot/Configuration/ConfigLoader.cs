using System.Text.Json;

namespace CopyBot.Configuration;

/// <summary>
/// Loads <see cref="CopyBotConfig"/> from a JSON file.
/// Resolution order for the config path:
///   1. an explicit path (--config &lt;path&gt;),
///   2. <c>config.json</c> next to the executable,
///   3. <c>%ProgramData%\CopyBot\config.json</c>.
/// </summary>
public static class ConfigLoader
{
    public static readonly string ProgramDataConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CopyBot", "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var exeDirConfig = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(exeDirConfig))
            return exeDirConfig;

        return ProgramDataConfigPath;
    }

    public static CopyBotConfig Load(string? explicitPath = null)
    {
        var path = ResolvePath(explicitPath);
        if (!File.Exists(path))
        {
            var config = new CopyBotConfig();
            config.LoadWarning = $"Config file '{path}' was not found; using built-in defaults.";
            return config;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<CopyBotConfig>(json, Options) ?? new CopyBotConfig();
            Normalize(config);
            return config;
        }
        catch (Exception ex)
        {
            var config = new CopyBotConfig();
            config.LoadWarning = $"Failed to parse config file '{path}': {ex.Message} Using built-in defaults.";
            return config;
        }
    }

    private static void Normalize(CopyBotConfig config)
    {
        config.SourceDriveTypes ??= new List<string> { "Removable" };
        config.Log ??= new CopyBotConfig.LogConfig();
        config.Copy ??= new CopyBotConfig.CopyConfig();
        config.Copy.ExcludedDirectoryNames ??= new List<string>();

        if (string.IsNullOrWhiteSpace(config.BackupRootFolder))
            config.BackupRootFolder = @"C:\Backups";

        if (string.IsNullOrWhiteSpace(config.Log.Directory))
            config.Log.Directory = @"C:\ProgramData\CopyBot\Logs";

        if (string.IsNullOrWhiteSpace(config.Log.FileNamePattern))
            config.Log.FileNamePattern = "copybot-{date}.log";

        if (string.IsNullOrWhiteSpace(config.Log.EventLogSource))
            config.Log.EventLogSource = "Windows Shadow Sync Service";

        if (string.IsNullOrWhiteSpace(config.Log.EventLogName))
            config.Log.EventLogName = "Application";

        if (string.IsNullOrWhiteSpace(config.Copy.SubfolderNameFormat))
            config.Copy.SubfolderNameFormat = "{name}_{date}";

        config.Copy.Included ??= new List<string>();
        config.Copy.Excluded ??= new List<string>();
    }
}