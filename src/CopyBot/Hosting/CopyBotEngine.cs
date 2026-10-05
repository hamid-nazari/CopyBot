using CopyBot.Configuration;
using CopyBot.Logging;
using CopyBot.Services;

namespace CopyBot.Hosting;

/// <summary>
/// Wires the WMI drive monitor to the copy engine and owns the logger and the
/// service lifetime token. Shared by both the Windows service host and the console
/// (debug) host.
/// </summary>
public sealed class CopyBotEngine : IDisposable
{
    private readonly CopyBotConfig _config;
    private readonly ActivityLogger _logger;
    private readonly CopyEngine _copyEngine;
    private readonly RemovableDriveMonitor _monitor;
    private readonly CancellationTokenSource _stopCts = new();
    private bool _started;

    public CopyBotEngine(CopyBotConfig config)
    {
        _config = config;
        _logger = new ActivityLogger(config.Log);

        if (!string.IsNullOrWhiteSpace(config.LoadWarning))
            _logger.Warn(config.LoadWarning);

        _monitor = new RemovableDriveMonitor();
        _monitor.VolumeEvent += OnVolumeEvent;

        _copyEngine = new CopyEngine(config, _logger);
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _logger.Info(
            "CopyBot (Windows Shadow Sync Service) starting. " +
            $"Backup root: '{_config.BackupRootFolder}', " +
            $"watched drive types: {string.Join(", ", _config.SourceDriveTypes)}, " +
            $"copy delay: {_config.CopyDelaySeconds:0.###}s.");

        _monitor.Start();
        _logger.Info("Removable drive change watcher started.");
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _logger.Info("Service stopping; cancelling any in-progress copies.");
        _stopCts.Cancel();
        _monitor.Stop();
        _copyEngine.CancelAll();
        _logger.Info("Service stopped.");
    }

    private void OnVolumeEvent(RemovableVolumeEvent evt)
    {
        if (_stopCts.IsCancellationRequested)
            return;

        if (evt.IsArrival)
        {
            _logger.Info($"Removable media detected on volume '{evt.DriveLetter}'.");
            _ = Task.Run(() => _copyEngine.HandleArrivalAsync(evt.DriveLetter, _stopCts.Token),
                _stopCts.Token);
        }
        else if (evt.IsRemoval)
        {
            _logger.Info($"Removable media removed on volume '{evt.DriveLetter}'.");
            _copyEngine.HandleRemoval(evt.DriveLetter);
        }
    }

    public void Dispose()
    {
        Stop();
        _monitor.VolumeEvent -= OnVolumeEvent;
        _monitor.Dispose();
        _logger.Dispose();
    }
}