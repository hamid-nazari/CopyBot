namespace CopyBot.Services;

/// <summary>
/// Snapshot of a <c>Win32_VolumeChangeEvent</c> raised by the WMI watcher.
/// EventType 2 = drive/media arrived, EventType 3 = drive/media removed.
/// </summary>
public readonly record struct RemovableVolumeEvent(int EventType, string DriveLetter)
{
    public const int Arrival = 2;
    public const int Removal = 3;

    public bool IsArrival => EventType == Arrival;
    public bool IsRemoval => EventType == Removal;
}