using System.Runtime.InteropServices;
using System.Text;

namespace CopyBot.Native;

/// <summary>
/// Thin P/Invoke wrappers for the Win32 volume APIs we need. Using native calls
/// keeps the sample free of extra WMI round-trips just to read a volume serial.
/// </summary>
internal static class NativeVolume
{
    private const uint MaxPath = 261;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        uint fileSystemNameSize);

    /// <summary>
    /// Returns a stable, short "UID" for a volume based on its serial number
    /// (e.g. <c>1A2B3C4D</c>). Falls back to the volume label, then "UNKNOWN".
    /// </summary>
    public static string GetVolumeUid(string rootPath)
    {
        var volumeName = new StringBuilder((int)MaxPath);
        var fileSystem = new StringBuilder((int)MaxPath);

        if (GetVolumeInformation(
                rootPath,
                volumeName,
                MaxPath,
                out uint serial,
                out _,
                out _,
                fileSystem,
                MaxPath) && serial != 0)
        {
            return serial.ToString("X8");
        }

        try
        {
            var drive = new DriveInfo(rootPath);
            if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                return drive.VolumeLabel;
        }
        catch
        {
            // Ignore and fall through.
        }

        return "UNKNOWN";
    }
}