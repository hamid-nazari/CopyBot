using CopyBot.Configuration;

namespace CopyBot.Hosting;

/// <summary>
/// Runs <see cref="CopyBotEngine"/> on the console so the service logic can be
/// exercised and debugged without installing it. Activated by running the executable
/// directly (or with <c>--console</c>).
/// </summary>
public static class ConsoleRunner
{
    public static void Run(CopyBotConfig config)
    {
        using var engine = new CopyBotEngine(config);
        using var exit = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.Cancel();
        };

        Console.WriteLine("CopyBot running in console (debug) mode. Press Ctrl+C to stop.");
        engine.Start();

        try
        {
            exit.Token.WaitHandle.WaitOne();
        }
        catch
        {
            // Ctrl+C fed the token above.
        }

        engine.Stop();
        Console.WriteLine("CopyBot stopped.");
    }
}