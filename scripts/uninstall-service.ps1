<#
.SYNOPSIS
  Stops and removes the CopyBot Windows service (service name "wsss-hnz-sbh",
  display name "Windows Shadow Sync Service").

.PARAMETER ServiceName
  Name of the Windows service. Default: wsss-hnz-sbh.

.PARAMETER RemoveInstallDir
  Also delete the folder that holds the running service binary. If -InstallDir is not
  supplied, this defaults to the folder of the registered service executable (which is
  where -FromSource-less installs keep the binaries / config).

.PARAMETER RemoveData
  Also delete the configuration and logs under %ProgramData%\CopyBot.

.PARAMETER InstallDir
  Folder that holds the service binaries. Default: the folder of the registered service
  executable, or %ProgramFiles%\CopyBot if the service is not installed.

.EXAMPLE
  .\uninstall-service.ps1
  .\uninstall-service.ps1 -RemoveInstallDir -RemoveData
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "wsss-hnz-sbh",
    [switch]$RemoveInstallDir,
    [switch]$RemoveData,
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"

# --- Elevation check -------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated (Administrator) PowerShell prompt."
}

# --- Helper: extract the executable path from a service PathName ------------
function Get-AbsoluteServiceExePath {
    param([string]$PathName)

    $trimmed = $PathName.Trim()
    if ($trimmed.Length -eq 0) { return $null }

    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -gt 0) { return $trimmed.Substring(1, $end - 1) }
        return $trimmed.Trim('"')
    }

    $space = $trimmed.IndexOf(' ')
    if ($space -gt 0) { return $trimmed.Substring(0, $space) }
    return $trimmed
}

# --- Discover the folder the service binary lives in ------------------------
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$serviceDir = $null

if ($svc) {
    try {
        $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
        $exePath = Get-AbsoluteServiceExePath $wmi.PathName
        if ($exePath -and (Test-Path $exePath)) {
            $serviceDir = Split-Path -Parent $exePath
        }
    } catch {
        $serviceDir = $null
    }
}

# --- Resolve the cleanup folder ---------------------------------------------
if (-not $InstallDir) {
    $InstallDir = if ($serviceDir) { $serviceDir } else { Join-Path $env:ProgramFiles "CopyBot" }
}
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

# --- Remove the service -----------------------------------------------------
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Write-Host "==> Stopping service '$ServiceName'" -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
    }
    Write-Host "==> Removing service '$ServiceName'" -ForegroundColor Cyan
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete service (exit $LASTEXITCODE)."
    }
    Start-Sleep -Seconds 2
    Write-Host "==> Service '$ServiceName' removed." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
}

# --- Optional cleanup -------------------------------------------------------
if ($RemoveInstallDir -and (Test-Path $InstallDir)) {
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "==> Removed install directory: $InstallDir" -ForegroundColor Green
}

if ($RemoveData) {
    $data = Join-Path $env:ProgramData "CopyBot"
    if (Test-Path $data) {
        Remove-Item -Path $data -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "==> Removed program data: $data" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Uninstall complete." -ForegroundColor Green