using CopyBot.Configuration;
using CopyBot.Logging;
using CopyBot.Services;

namespace CopyBot.Hosting;

/// <summary>
/// One-off, command-line directory sync used to exercise and validate the exact copy
/// and smart skip/overwrite logic without needing a removable drive. Usage:
/// <c>CopyBot.exe --sync &lt;sourceDir&gt; &lt;targetDir&gt;</c>.
/// </summary>
public static class DiagnosticSync
{
    public static async Task<int> RunAsync(CopyBotConfig config, string sourceDir, string targetDir)
    {
        Console.WriteLine($"Diagnostic sync: '{sourceDir}' -> '{targetDir}'");

        using var logger = new ActivityLogger(config.Log);
        var engine = new CopyEngine(config, logger);

        var summary = await engine.SyncDirectoryAsync(
            Path.GetFullPath(sourceDir),
            Path.GetFullPath(targetDir),
            $"diagnostic source '{sourceDir}'",
            CancellationToken.None);

        Console.WriteLine(
            $"Done. Copied={summary.Copied}, Skipped={summary.Skipped}, Failed={summary.Failed}, " +
            $"Bytes={summary.BytesCopied}, Elapsed={summary.Elapsed.TotalSeconds:F1}s");

        return summary.Failed == 0 ? 0 : 1;
    }
}