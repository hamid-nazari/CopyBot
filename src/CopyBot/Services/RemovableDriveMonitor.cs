using System.Management;

namespace CopyBot.Services;

/// <summary>
/// Listens for <c>Win32_VolumeChangeEvent</c> WMI events (drive/media arrival and
/// removal). WMI is used instead of <see cref="FileSystemWatcher"/> because it works
/// in Session 0 where the service runs and it reliably reports drive letters.
/// </summary>
public sealed class RemovableDriveMonitor : IDisposable
{
    private readonly ManagementEventWatcher _watcher;

    /// <summary>Raised (on a WMI worker thread) for every matching volume change.</summary>
    public event Action<RemovableVolumeEvent>? VolumeEvent;

    public RemovableDriveMonitor()
    {
        var query = new WqlEventQuery(
            "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
        _watcher = new ManagementEventWatcher(query);
        _watcher.EventArrived += OnEventArrived;
    }

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["EventType"] is not { } eventTypeObj)
                return;

            int eventType = Convert.ToInt32(eventTypeObj, System.Globalization.CultureInfo.InvariantCulture);
            string driveName = (e.NewEvent["DriveName"] as string) ?? string.Empty;
            string driveLetter = driveName.TrimEnd(':').Trim();

            if (driveLetter.Length == 0)
                return;

            VolumeEvent?.Invoke(new RemovableVolumeEvent(eventType, driveLetter));
        }
        catch
        {
            // Malformed event; ignore it.
        }
    }

    public void Dispose()
    {
        _watcher.Stop();
        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
    }
}