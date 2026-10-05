using System.ServiceProcess;
using CopyBot.Configuration;
using CopyBot.Hosting;

namespace CopyBot;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? configPath = ParseConfigPath(args);
        var config = ConfigLoader.Load(configPath);

        // Diagnostic one-off directory sync: --sync <sourceDir> <targetDir>
        if (TryParseSync(args, out string? syncSource, out string? syncTarget))
        {
            return DiagnosticSync.RunAsync(config, syncSource!, syncTarget!).GetAwaiter().GetResult();
        }

        bool consoleMode = Environment.UserInteractive
                           || args.Contains("--console", StringComparer.OrdinalIgnoreCase);

        if (consoleMode)
        {
            ConsoleRunner.Run(config);
            return 0;
        }

        // Running under the Service Control Manager.
        ServiceBase.Run(new CopyBotService(config));
        return 0;
    }

    private static bool TryParseSync(string[] args, out string? sourceDir, out string? targetDir)
    {
        sourceDir = null;
        targetDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--sync", StringComparison.OrdinalIgnoreCase)
                && i + 2 < args.Length)
            {
                sourceDir = args[i + 1];
                targetDir = args[i + 2];
                return true;
            }
        }

        return false;
    }

    private static string? ParseConfigPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];

            const string prefix = "--config=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return args[i][prefix.Length..];
        }

        return null;
    }
}