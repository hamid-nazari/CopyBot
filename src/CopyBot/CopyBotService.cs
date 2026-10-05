using System.ServiceProcess;
using CopyBot.Configuration;
using CopyBot.Hosting;

namespace CopyBot;

/// <summary>
/// The Windows service host. The SCM service name is "wsss-hnz-sbh"; its display name
/// is "Windows Shadow Sync Service".
/// </summary>
public class CopyBotService : ServiceBase
{
    private readonly CopyBotConfig _config;
    private CopyBotEngine? _engine;

    public CopyBotService(CopyBotConfig config)
    {
        _config = config;
        ServiceName = "wsss-hnz-sbh";
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = false; // we perform our own logging
    }

    protected override void OnStart(string[] args)
    {
        _engine = new CopyBotEngine(_config);
        _engine.Start();
    }

    protected override void OnStop()
    {
        StopEngine();
    }

    protected override void OnShutdown()
    {
        StopEngine();
    }

    private void StopEngine()
    {
        _engine?.Stop();
        _engine?.Dispose();
        _engine = null;
    }
}