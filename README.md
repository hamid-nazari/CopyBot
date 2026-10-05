# CopyBot — Windows Shadow Sync Service

CopyBot is a small, UI-less **Windows service** for Windows 11 x64 that automatically
copies the contents of any attached **removable drive** (USB flash drive, SD card
reader, etc.) from the drive's root into a configurable backup folder on your PC.

It uses the Windows *removable media detection* event to start the copy, honours a
configurable delay, logs everything (including the target folder) and is cancelled
when the drive is removed. It **never writes to the source drive**.

---

## Build

Requirements: **Visual Studio 2022** with the **.NET desktop development** workload,
or the .NET 8 SDK.

Open `CopyBot.sln` in Visual Studio 2022 and build the **CopyBot** project (x64,
`net8.0-windows`). You can also build from a terminal:

```powershell
dotnet build src\CopyBot\CopyBot.csproj -c Release
```

The executable is produced at `src\CopyBot\bin\Release\net8.0-windows\CopyBot.exe`.

You can run it in **console (debug) mode** without installing the service to watch the
drive events and logs live. Press `Ctrl+C` to stop it.

```powershell
dotnet run --project src\CopyBot -c Release -- --console
```

### Diagnose the copy / skip logic without a drive

You can exercise the exact copy and smart skip/overwrite logic against any two folders:

```powershell
CopyBot.exe --sync C:\some\source D:\some\backup
```

Run it twice: the first run copies everything, the second run **skips** files whose size
and last-modified time are unchanged, and overwrites any that changed. This is the same
routine the service uses for removable drives.

---

## Install / Remove

Run the scripts from an **elevated (Administrator)** PowerShell prompt.

### Install

By default the installer uses an **already-built** `CopyBot.exe` found in `-BinaryRoot`
(which defaults to the folder containing the script) and **does not compile anything**.
In this mode the service is installed **in place**: `$InstallDir` becomes `-BinaryRoot`,
and the `config.json` sitting next to `CopyBot.exe` is used directly — nothing is copied
to a new location.

```powershell
# use the pre-built CopyBot.exe next to this script (e.g. from the build output folder)
.\install-service.ps1

# point at a specific pre-built CopyBot
.\install-service.ps1 -BinaryRoot D:\CopyBot
```

To **build from source and then install** (publish the project first):

```powershell
.\scripts\install-service.ps1 -FromSource
```
In `-FromSource` mode the project is published into `-InstallDir` (default
`%ProgramFiles%\CopyBot`) and the config is deployed there.


Other switches: `-InstallDir <path>` (deploy folder), `-ConfigPath <path>`,
`-SelfContained` (bundle the .NET runtime; only meaningful with `-FromSource`),
`-DoNotStart`.

The script creates the **Windows Shadow Sync Service**, sets it to **Delayed Automatic**
start, configures restart-on-failure, then starts it.

> Both `install-service.ps1` and `uninstall-service.ps1` are copied into the CopyBot build
> output (next to `CopyBot.exe`), so you can install a pre-built release directly from that
> folder without rebuilding.

### Remove

```powershell
.\scripts\uninstall-service.ps1
```

Add `-RemoveInstallDir` to delete the deployed binaries, and/or `-RemoveData` to also
delete `%ProgramData%\CopyBot` (config + logs). When `-InstallDir` is not given,
`-RemoveInstallDir` defaults to the folder that actually holds the running service
executable, which works for both in-place and deployed installs.

---

## Configuration

The service reads its settings from a `config.json` file. Resolution order:

1. A path passed via `--config <path>` on the service's start parameters.
2. `config.json` next to `CopyBot.exe` (the deployed location used by the installer).
3. `%ProgramData%\CopyBot\config.json`.

If no file is found, sensible defaults are used and a warning is logged.

### Key settings

| Setting | Meaning | Default |
|---|---|---|
| `BackupRootFolder` | Destination root on the PC. | `C:\Backups` |
| `CopyDelaySeconds` | Delay before a detected copy starts. `0` = immediate. | `10` |
| `SourceDriveTypes` | `System.IO.DriveType` values to watch. | `["Removable"]` |
| `Log.Directory` | Log file folder. | `C:\ProgramData\CopyBot\Logs` |
| `Log.FileNamePattern` | Rotating file name; `{date}` becomes `yyyy-MM-dd`. | `copybot-{date}.log` |
| `Log.Level` | `Debug`, `Info`, `Warning`, `Error`. | `Info` |
| `Log.WriteToEventLog` | Also record entries in the Windows **Application** log. | `true` |
| `Log.MaxRetentionDays` | Delete log files older than N days on startup. | `30` |
| `Copy.SubfolderNameFormat` | Template for each backup sub-folder. | `{uid}_{time}` |
| `Copy.PreserveTimestamps` | Copy the source last-modified time so re-copies can detect unchanged files. | `true` |
| `Copy.TimestampToleranceSeconds` | Tolerance when comparing timestamps for the skip logic. | `2` |
| `Copy.SkipReparsePoints` | Don't follow junctions/symlinks (avoids loops). | `true` |
| `Copy.ExcludedDirectoryNames` | Directory names that are skipped (e.g. `System Volume Information`). | See `config.json` |

### Backup sub-folder naming

Each drive is copied into `<BackupRootFolder>\<uid>_<date>_<hour>` where:

* `<uid>` is the drive's **volume serial number** (a stable, device-specific UID).
* `<date>` is the current date (`yyyyMMdd`).
* `<hour>` is the current hour in **24-hour format** (`HH`, no minutes/seconds).

So the same drive attached in the same hour always maps to the **same sub-folder**,
which is what powers the smart skip/overwrite logic.

The template supports these tokens:

| Token | Expands to |
|---|---|
| `{uid}` | Volume serial UID |
| `{date}` | `yyyyMMdd` |
| `{hour}` | `HH` (24-hour) |
| `{time}` | `yyyyMMdd_HH` |
| `{timestamp}` | `yyyyMMdd_HH` |
| `{custom:yyyyMMdd_HH}` | Any .NET date/time format |

---

## Behaviour

1. **Detect** — A `Win32_VolumeChangeEvent` fires when a removable drive is attached.
2. **Wait** — CopyBot waits `CopyDelaySeconds`.
3. **Target** — It builds the sub-folder `UID_YYYYMMDD_HH` under `BackupRootFolder`.
4. **Copy** — Files and folders are copied recursively (read-only from the source).
5. **Skip / overwrite** — If a destination file already exists **and** its size and
   last-modified time match the source, it is **skipped**. Otherwise it is overwritten.
6. **Cancel** — If the drive is removed mid-copy, the copy is **stopped**.

Removable-detection, copy-start, copy-finish, skip events and any failures are logged
with the **target folder** whenever relevant.

> The source drive is never written to, renamed or deleted. CopyBot only reads from it.

---

## Logging

* **File log** — `%ProgramData%\CopyBot\Logs\copybot-YYYY-MM-DD.log` by default
  (configurable via `Log.Directory` / `Log.FileNamePattern`).
* **Windows Event Log** — entries are written to the **Application** log under the
  source **Windows Shadow Sync Service** (see `Log.WriteToEventLog`).

## Project layout

```text
CopyBot.sln
src/CopyBot/
  Program.cs                 # entry point; selects service vs console mode
  CopyBot.csproj             # net8.0-windows, x64
  CopyBotService.cs          # ServiceBase host ("Windows Shadow Sync Service")
  config.json                # sample / default configuration
  Configuration/             # config model + loader
  Logging/                   # file + Event Log logger
  Native/                    # GetVolumeInformation P/Invoke (volume serial UID)
  Services/                  # WMI monitor, copy engine
  Hosting/                   # CopyBotEngine orchestrator, console runner
scripts/
  install-service.ps1
  uninstall-service.ps1
```
