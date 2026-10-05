using System.Diagnostics;
using System.Text.RegularExpressions;
using CopyBot.Configuration;
using CopyBot.Logging;
using CopyBot.Native;

namespace CopyBot.Services;

/// <summary>
/// Orchestrates the copy for a single removable drive. It delays before copying
/// (configurable), builds the target sub-folder (UID + hour timestamp), performs a
/// read-only copy, skips files whose size + last-modified time already match the
/// destination, and can be cancelled when the drive is removed.
/// The source drive is never written to.
/// </summary>
public sealed class CopyEngine
{
    private readonly CopyBotConfig _config;
    private readonly ActivityLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _active =
        new(StringComparer.OrdinalIgnoreCase);

    public CopyEngine(CopyBotConfig config, ActivityLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Handles a drive/media arrival: waits, then copies after the configured delay.</summary>
    public async Task HandleArrivalAsync(string driveLetter, CancellationToken appToken)
    {
        var cts = RegisterActive(driveLetter, appToken);
        string? backupFolder = null;

        try
        {
            var drive = await WaitForDriveAsync(driveLetter, cts.Token);
            if (drive is null)
            {
                _logger.Warn($"Drive '{driveLetter}' was detected but could not be opened. Copy skipped.");
                return;
            }

            if (!IsAllowedDriveType(drive))
            {
                _logger.Info(
                    $"Volume '{driveLetter}' has type {drive.DriveType}, which is not in configured " +
                    $"SourceDriveTypes ({string.Join(", ", _config.SourceDriveTypes)}). Copy skipped.");
                return;
            }

            if (!drive.IsReady)
            {
                _logger.Warn($"Volume '{driveLetter}' is not ready. Copy skipped.");
                return;
            }

            if (_config.CopyDelaySeconds > 0)
            {
                _logger.Info(
                    $"Removable drive '{driveLetter}' detected. Waiting {_config.CopyDelaySeconds:0.###}s before copying…");
                await Task.Delay(TimeSpan.FromSeconds(_config.CopyDelaySeconds), cts.Token);
            }

            string uid = NativeVolume.GetVolumeUid(drive.RootDirectory.FullName);
            string label = NativeVolume.GetVolumeLabel(drive.RootDirectory.FullName);
            backupFolder = BuildBackupFolder(uid, label);

            if (string.IsNullOrWhiteSpace(uid) || uid == "UNKNOWN")
            {
                _logger.Warn(
                    $"Could not determine a stable UID for volume '{driveLetter}'; using 'UNKNOWN' in the target folder name.");
            }

            await CopyDriveAsync(drive, backupFolder, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"Copy for drive '{driveLetter}' was cancelled (drive removed or service stopping).");
        }
        catch (Exception ex)
        {
            var where = backupFolder is null ? "" : $" (target folder: '{backupFolder}')";
            _logger.Error($"Copy for drive '{driveLetter}' failed{where}: {ex.Message}");
        }
        finally
        {
            UnregisterActive(driveLetter, cts);
        }
    }

    /// <summary>Stops any in-progress copy for the given drive letter.</summary>
    public void HandleRemoval(string driveLetter)
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_active.TryGetValue(driveLetter, out cts))
                cts?.Cancel();
        }

        if (cts is not null)
            _logger.Info($"Volume '{driveLetter}' was removed; in-progress copy was cancelled.");
    }

    public void CancelAll()
    {
        List<CancellationTokenSource> copies;
        lock (_gate)
        {
            copies = _active.Values.ToList();
            _active.Clear();
        }

        foreach (var cts in copies)
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        }
    }

    private CancellationTokenSource RegisterActive(string driveLetter, CancellationToken appToken)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(driveLetter, out var existing))
            {
                _logger.Warn($"Drive '{driveLetter}' was re-detected while a copy is active; restarting copy.");
                existing.Cancel();
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
            _active[driveLetter] = cts;
            return cts;
        }
    }

    private void UnregisterActive(string driveLetter, CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(driveLetter, out var current) && ReferenceEquals(current, cts))
                _active.Remove(driveLetter);
        }

        cts.Dispose();
    }

    private static async Task<DriveInfo?> WaitForDriveAsync(string driveLetter, CancellationToken ct)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15) && !ct.IsCancellationRequested)
        {
            try
            {
                var drive = new DriveInfo(driveLetter + ":\\");
                if (drive.DriveType != DriveType.NoRootDirectory && drive.IsReady)
                    return drive;
            }
            catch
            {
                // Drive not fully mounted yet.
            }

            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        return null;
    }

    private bool IsAllowedDriveType(DriveInfo drive)
    {
        var wanted = _config.SourceDriveTypes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return wanted.Contains(drive.DriveType.ToString());
    }

    private string BuildBackupFolder(string uid, string label)
    {
        var template = _config.Copy.SubfolderNameFormat;
        var now = DateTime.Now;

        var name = ResolveTemplate(template, uid, label, now);
        var root = Path.GetFullPath(_config.BackupRootFolder);
        return Path.Combine(root, name);
    }

    private static string ResolveTemplate(string template, string uid, string label, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "{name}_{date}";

        // {name} maps to the volume label; fall back to "noname_<uid>" when missing.
        var name = string.IsNullOrWhiteSpace(label) ? $"noname_{uid}" : label;

        var result = template
            .Replace("{uid}", uid)
            .Replace("{name}", name)
            .Replace("{date}", now.ToString("yyyyMMdd"))
            .Replace("{hour}", now.ToString("HH"))
            .Replace("{time}", now.ToString("yyyyMMdd_HH"))
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HH"));

        // Support a custom .NET date/time segment: {custom:yyyyMMdd_HH}
        result = Regex.Replace(result, @"\{custom:(?<fmt>[^}]+)\}",
            m => now.ToString(m.Groups["fmt"].Value, System.Globalization.CultureInfo.InvariantCulture));

        return result;
    }

    private async Task CopyDriveAsync(DriveInfo drive, string backupFolder, CancellationToken ct)
    {
        await SyncDirectoryAsync(drive.RootDirectory.FullName, backupFolder, $"drive '{drive.Name}'", ct);
    }

    /// <summary>
    /// Copies the contents of <paramref name="sourceRoot"/> into <paramref name="backupFolder"/>
    /// recursively, applying the smart skip/overwrite rule. Shared by the live service and by the
    /// <c>--sync</c> diagnostic mode. Never writes to the source.
    /// </summary>
    internal async Task<CopySyncSummary> SyncDirectoryAsync(
        string sourceRoot,
        string backupFolder,
        string sourceLabel,
        CancellationToken ct)
    {
        _logger.Info($"Copy started for {sourceLabel} → target folder '{backupFolder}'.");
        Directory.CreateDirectory(backupFolder);

        int copied = 0, skipped = 0, failed = 0, excluded = 0, notIncluded = 0;
        long totalBytes = 0;
        var watch = Stopwatch.StartNew();

        foreach (var (source, length) in EnumerateFiles(sourceRoot, ct))
        {
            ct.ThrowIfCancellationRequested();

            string relative = Path.GetRelativePath(sourceRoot, source);
            string destination = Path.Combine(backupFolder, relative);

            // Apply file-path include/exclude glob rules before anything else.
            string relPath = relative.Replace('\\', '/');
            if (GlobMatcher.IsMatchAny(_config.Copy.Excluded, relPath))
            {
                excluded++;
                bool alsoIncluded = _config.Copy.Included.Count > 0
                                    && GlobMatcher.IsMatchAny(_config.Copy.Included, relPath);
                var note = alsoIncluded ? " (Excluded takes precedence over Included.)" : "";
                _logger.Info($"Excluded by pattern: '{relative}'{note}");
                continue;
            }

            if (_config.Copy.Included.Count > 0 && !GlobMatcher.IsMatchAny(_config.Copy.Included, relPath))
            {
                notIncluded++;
                _logger.Info($"Not matched by Included patterns: '{relative}'");
                continue;
            }

            if (ShouldSkip(source, destination))
            {
                skipped++;
                _logger.Info($"Skipped unchanged file: '{relative}' (same size and last-modified time).");
                continue;
            }

            try
            {
                await CopyFileAsync(source, destination, ct);
                copied++;
                totalBytes += length;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Error($"Failed to copy '{relative}' to '{destination}': {ex.Message}");
            }
        }

        _logger.Info(
            $"Copy completed for {sourceLabel} → target folder '{backupFolder}' in " +
            $"{watch.Elapsed.TotalSeconds:F1}s. Files copied: {copied}, unchanged skipped: {skipped}, " +
            $"excluded by pattern: {excluded}, not matched by Included: {notIncluded}, " +
            $"failed: {failed}, bytes copied: {totalBytes}.");

        return new CopySyncSummary(copied, skipped, failed, excluded, notIncluded, totalBytes, watch.Elapsed);
    }

    private IEnumerable<(string Path, long Length)> EnumerateFiles(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string directory = stack.Pop();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Cannot enumerate directory '{directory}': {ex.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Cannot read attributes of '{entry}': {ex.Message}");
                    continue;
                }

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory)
                {
                    if (_config.Copy.SkipReparsePoints && (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        _logger.Info($"Skipped reparse point (junction/symlink) directory: '{entry}'.");
                        continue;
                    }

                    var dirName = Path.GetFileName(entry);
                    if (IsExcludedDirectory(dirName))
                    {
                        _logger.Info($"Skipped excluded directory: '{entry}'.");
                        continue;
                    }

                    stack.Push(entry);
                }
                else
                {
                    FileInfo? info = null;
                    try
                    {
                        info = new FileInfo(entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Cannot read file metadata for '{entry}': {ex.Message}");
                        continue;
                    }

                    if (info is not null)
                        yield return (entry, info.Length);
                }
            }
        }
    }

    private bool IsExcludedDirectory(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return _config.Copy.ExcludedDirectoryNames.Any(
            e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the destination file already exists and its size and
    /// last-modified time match the source (within tolerance), so it can be skipped.
    /// </summary>
    private bool ShouldSkip(string source, string destination)
    {
        try
        {
            var dest = new FileInfo(destination);
            if (!dest.Exists)
                return false;

            var src = new FileInfo(source);
            if (src.Length != dest.Length)
                return false;

            var tolerance = TimeSpan.FromSeconds(_config.Copy.TimestampToleranceSeconds);
            var delta = Math.Abs((src.LastWriteTimeUtc - dest.LastWriteTimeUtc).TotalSeconds);
            return delta <= tolerance.TotalSeconds;
        }
        catch
        {
            // If we can't compare, be safe and copy.
            return false;
        }
    }

    private async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        int buffer = _config.Copy.BufferSizeBytes;
        int retries = Math.Max(1, _config.Copy.MaxRetryCount);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var sourceStream = new FileStream(
                    source,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    buffer,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                await using (var destinationStream = new FileStream(
                    destination,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    buffer,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await sourceStream.CopyToAsync(destinationStream, buffer, ct);
                }

                if (_config.Copy.PreserveTimestamps)
                {
                    var sourceTime = File.GetLastWriteTimeUtc(source);
                    File.SetLastWriteTimeUtc(destination, sourceTime);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < retries && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
        }
    }
}

/// <summary>Result of one directory sync (used by logging and the <c>--sync</c> diagnostic).</summary>
public sealed record CopySyncSummary(
    int Copied,
    int Skipped,
    int Failed,
    int Excluded,
    int NotIncluded,
    long BytesCopied,
    TimeSpan Elapsed);