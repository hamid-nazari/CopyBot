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
## Release packaging (optional)

Packaging a deployable ZIP is an **optional** step you run only when you are happy with
a build. It is never produced by a normal build.

The ZIP is named after the **version** set on the project, e.g. `CopyBot-1.2.0-win-x64.zip`.
The version is a single source of truth in `src/CopyBot/CopyBot.csproj`:

```xml
<Version>1.2.0</Version>
```

Two equivalent ways to create the archive:

```powershell
# 1) As an MSBuild stage (builds first, then packages)
dotnet build src\CopyBot\CopyBot.csproj -c Release /t:CreateReleaseZip -p:CreateReleaseZip=true

# 2) Directly via the script (package an existing build/publish output)
.\scripts\package-zip.ps1                # default: src\CopyBot\bin\Release\net8.0-windows
.\scripts\package-zip.ps1 -Source .\release -ExcludePdb
```

The archive is written to `dist\` (git-ignored). Options for `package-zip.ps1`:

| Parameter | Meaning | Default |
|---|---|---|
| `-Source` | Folder containing the built `CopyBot.exe` | `src\CopyBot\bin\Release\net8.0-windows` |
| `-OutputDir` | Folder for the ZIP | `<repo>\dist` |
| `-ZipFileName` | Archive name; supports `{version}` | `CopyBot-{version}-win-x64.zip` |
| `-ExcludePdb` | Omit `*.pdb` symbol files | off |

> The script logs and skips any nested folder (e.g. a stale `win-x64` publish output) that
> duplicates the deployable files, so the release archive always contains a single
> `CopyBot.exe` at the root.

### Publish a clean build first (recommended)

```powershell
dotnet publish src\CopyBot\CopyBot.csproj -c Release -r win-x64 -o .\release
.\scripts\package-zip.ps1 -Source .\release -ExcludePdb
```

### GitHub release

1. Bump `<Version>` in `src/CopyBot/CopyBot.csproj`.

3. The included `.github/workflows/release.yml` builds the project, packages the ZIP and
   attaches it to a GitHub Release as a downloadable artifact (`dist/*.zip`,
   `CopyBot-<version>-win-x64.zip`).

If you prefer to attach the archive manually, run the packaging commands above and upload
the generated `dist/*.zip` to the release you created for that tag.

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

The script creates the **wsss-hnz-sbh** service (display name **Windows Shadow Sync
Service**), sets it to **Delayed Automatic** start, configures restart-on-failure, then
starts it.

> When installing **in place** (without `-FromSource`), the script updates the
> `Log.Directory` in the deployed `config.json` to a `<InstallDir>\Logs` folder (creating
> the folder if needed), so logs stay next to the installed binaries. An existing custom
> `Log.Directory` value is left untouched.

> The service **Description** is taken from `CopyBot.exe`'s **Product Name** (set via
> `<Product>` in `CopyBot.csproj`), falling back to a description constant if it is empty.
>
> Both `install-service.ps1`/`install-service.cmd` and `uninstall-service.ps1`/
> `uninstall-service.cmd` are copied into the CopyBot build output (next to `CopyBot.exe`),
> so you can install a pre-built release directly from that folder without rebuilding.

### Remove

```powershell
.\scripts\uninstall-service.ps1
```

Add `-RemoveInstallDir` to delete the deployed binaries, and/or `-RemoveData` to also
delete `%ProgramData%\CopyBot` (config + logs). When `-InstallDir` is not given,
`-RemoveInstallDir` defaults to the folder that actually holds the running service
executable, which works for both in-place and deployed installs.

### Batch wrappers & helper scripts

Every `scripts/*.ps1` has a matching `.cmd` wrapper that runs it with
`pwsh -NoProfile -ExecutionPolicy Bypass` (e.g. `scripts\install-service.cmd`,
`scripts\release.cmd`, `scripts\view-logs.cmd`).

* `scripts\release.ps1` / `release.cmd` — builds, publishes, packages the ZIP, then removes
  the publish folder.
* `scripts\package-zip.ps1` / `package-zip.cmd` — creates the release ZIP on demand.
* `scripts\view-logs.ps1` / `view-logs.cmd` — prints the CopyBot Application-log events and
  opens Windows Event Viewer (filter on source `Windows Shadow Sync Service`).

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
| `Copy.SubfolderNameFormat` | Template for each backup sub-folder. | `{name}_{date}` |
| `Copy.PreserveTimestamps` | Copy the source last-modified time so re-copies can detect unchanged files. | `true` |
| `Copy.TimestampToleranceSeconds` | Tolerance when comparing timestamps for the skip logic. | `2` |
| `Copy.SkipReparsePoints` | Don't follow junctions/symlinks (avoids loops). | `true` |
| `Copy.ExcludedDirectoryNames` | Directory names that are skipped (e.g. `System Volume Information`). | See `config.json` |
| `Copy.Included` | Glob patterns; when non-empty, only matching file paths are copied. | `[]` (all) |
| `Copy.Excluded` | Glob patterns; matching file paths are skipped (takes precedence over `Included`). | `[]` (none) |

### Backup sub-folder naming

Each drive is copied into `<BackupRootFolder>\<name>_<date>` (default template) where:

* `<name>` is the drive's **volume label** (e.g. `USB`); it falls back to `noname_<uid>`
  when the label cannot be read or is empty.
* `<uid>` is the drive's **volume serial number** (a stable, device-specific UID).
* `<date>` is the current date (`yyyyMMdd`).

> Use `{uid}_{time}` (`UID_YYYYMMDD_HH` — date + 24-hour hour, no minutes/seconds) if you
> want the same drive re-attached in the same hour to map to the same sub-folder, which
> powers the smart skip/overwrite logic.

The template supports these tokens:

| Token | Expands to |
|---|---|
| `{name}` | Volume label (falls back to `noname_<uid>`) |
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
3. **Target** — It builds the sub-folder `LABEL_YYYYMMDD` (or per `SubfolderNameFormat`)
   under `BackupRootFolder`.
4. **Filter** — `Excluded`/`Included` glob patterns decide which file paths are copied
   (`Excluded` wins when both match).
5. **Copy** — Files and folders are copied recursively (read-only from the source).
6. **Skip / overwrite** — If a destination file already exists **and** its size and
   last-modified time match the source, it is **skipped**. Otherwise it is overwritten.
7. **Cancel** — If the drive is removed mid-copy, the copy is **stopped**.

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
  CopyBotService.cs          # ServiceBase host (service name "wsss-hnz-sbh")
  config.json                # sample / default configuration
  Configuration/             # config model + loader
  Logging/                   # file + Event Log logger
  Native/                    # GetVolumeInformation P/Invoke (volume serial + label)
  Services/                  # WMI monitor, copy engine, glob matcher
  Hosting/                   # CopyBotEngine orchestrator, console runner
scripts/
  install-service.ps1        # + install-service.cmd wrapper
  uninstall-service.ps1      # + uninstall-service.cmd wrapper
  package-zip.ps1            # + package-zip.cmd wrapper
  release.ps1                # + release.cmd wrapper
  view-logs.ps1              # + view-logs.cmd wrapper
.github/workflows/release.yml
```
