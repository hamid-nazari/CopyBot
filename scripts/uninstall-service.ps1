<#
.SYNOPSIS
  Stops and removes the CopyBot Windows service ("Windows Shadow Sync Service").

.PARAMETER ServiceName
  Name of the Windows service. Default: Windows Shadow Sync Service.

.PARAMETER RemoveInstallDir
  Also delete the folder the binaries were deployed to.

.PARAMETER RemoveData
  Also delete the configuration and logs under %ProgramData%\CopyBot.

.PARAMETER InstallDir
  Folder the service binaries live in. Default: %ProgramFiles%\CopyBot.

.EXAMPLE
  .\uninstall-service.ps1
  .\uninstall-service.ps1 -RemoveInstallDir -RemoveData
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "Windows Shadow Sync Service",
    [switch]$RemoveInstallDir,
    [switch]$RemoveData,
    [string]$InstallDir = (Join-Path $env:ProgramFiles "CopyBot")
)

$ErrorActionPreference = "Stop"

# --- Elevation check -------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated (Administrator) PowerShell prompt."
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Write-Host "==> Stopping service '$ServiceName'" -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
    }
    Write-Host "==> Removing service '$ServiceName'" -ForegroundColor Cyan
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to delete service (exit $LASTEXITCODE)." }
    Start-Sleep -Seconds 2
    Write-Host "==> Service '$ServiceName' removed." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
}

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