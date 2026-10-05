<#
.SYNOPSIS
  Creates a deployable ZIP of the CopyBot build output (optional packaging stage).

.DESCRIPTION
  Zips the contents of the CopyBot output folder into a single release archive whose
  name includes the version baked into CopyBot.exe (e.g. CopyBot-1.0.0-win-x64.zip).

  This is an OPTIONAL step: it is NOT produced by a normal build. Run it only when you
  are happy with a build, then upload the ZIP to a GitHub release tag.

.PARAMETER Source
  Folder that contains the built CopyBot.exe. Default:
  src\CopyBot\bin\Release\net8.0-windows.

.PARAMETER OutputDir
  Folder where the ZIP is written. Default: <repo>\dist.

.PARAMETER ZipFileName
  Optional archive name. Supports the {version} token. Default:
  CopyBot-{version}-win-x64.zip.

.PARAMETER ExcludePdb
  Omit *.pdb symbol files from the archive (smaller release).

.EXAMPLE
  .\scripts\package-zip.ps1
  .\scripts\package-zip.ps1 -Source C:\publish\CopyBot -OutputDir C:\release
  .\scripts\package-zip.ps1 -ExcludePdb
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\src\CopyBot\bin\Release\net8.0-windows"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\dist"),
    [string]$ZipFileName,
    [switch]$ExcludePdb
)

$ErrorActionPreference = "Stop"

$Source = [System.IO.Path]::GetFullPath($Source)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

# --- Validate that a built executable exists ---------------------------------
$exe = Join-Path $Source "CopyBot.exe"
if (-not (Test-Path $exe)) {
    throw "CopyBot.exe was not found in '$Source'. Build or publish first (for example: `"dotnet publish src\CopyBot\CopyBot.csproj -c Release -r win-x64 -o <folder>`") and pass -Source."
}

# --- Read the version baked into the executable ------------------------------
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = $versionInfo.FileVersion }
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }

# Drop semver build metadata (the +<git-sha> suffix the SDK may append) and trim.
$plus = $version.IndexOf('+')
if ($plus -ge 0) { $version = $version.Substring(0, $plus) }
$version = $version.Trim()
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }

# --- Build the ZIP file name --------------------------------------------------
$safeVersion = $version -replace '[^0-9A-Za-z.\-_]', '-'
if (-not $ZipFileName) { $ZipFileName = "CopyBot-{version}-win-x64.zip" }
$zipName = $ZipFileName.Replace('{version}', $safeVersion)
$zipPath = Join-Path $OutputDir $zipName

# --- Guard against archiving the zip into itself ------------------------------
$sourceWithSep = $Source.TrimEnd('\') + '\'
$outputWithSep = $OutputDir.TrimEnd('\') + '\'
if ($outputWithSep.StartsWith($sourceWithSep, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDir '$OutputDir' is inside '$Source'. Choose an OutputDir outside the source folder (default: <repo>\dist)."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# --- Stage contents so we can drop PDBs and never include the zip itself ------
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("CopyBot-package-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    # Copy the top-level entries from Source. Any nested folder that itself contains
    # CopyBot.exe is a stale/duplicate RID publish output (e.g. 'win-x64') whose files
    # duplicate the root; exclude it so the release archive stays clean.
    Get-ChildItem -Path $Source -Force | ForEach-Object {
        $include = $true
        if ($_.PSIsContainer -and (Test-Path (Join-Path $_.FullName 'CopyBot.exe'))) {
            Write-Host "    Excluding redundant nested build folder: '$($_.FullName)'" -ForegroundColor Yellow
            $include = $false
        }
        if ($include) {
            Copy-Item -Path $_.FullName -Destination $staging -Recurse -Force
        }
    }

    if ($ExcludePdb) {
        Get-ChildItem -Path $staging -Recurse -Filter '*.pdb' -File | Remove-Item -Force
    }

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $fileCount = (Get-ChildItem -Path $staging -Recurse -File | Measure-Object).Count
    $sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

    Write-Host ""
    Write-Host "==> Created release archive : $zipPath" -ForegroundColor Green
    Write-Host "    Version                : $version"
    Write-Host "    Files archived          : $fileCount"
    Write-Host "    Size                   : $sizeMb MB"
} finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}