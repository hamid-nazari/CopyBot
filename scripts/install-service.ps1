<#
.SYNOPSIS
  Installs the CopyBot Windows service ("Windows Shadow Sync Service").

.DESCRIPTION
  Publishes the CopyBot project, copies the default configuration next to the
  executable, registers a Windows service and starts it. Run from an elevated
  PowerShell prompt (Administrator).

.PARAMETER InstallDir
  Folder the service binaries are deployed to. Default: %ProgramFiles%\CopyBot.

.PARAMETER ServiceName
  Name of the Windows service. Default: Windows Shadow Sync Service.

.PARAMETER DisplayName
  Human-friendly service display name.

.PARAMETER Description
  Description shown in Services.msc.

.PARAMETER ConfigPath
  Optional source config file to copy into InstallDir. Default: project config.json.

.PARAMETER SelfContained
  Publish a self-contained build (bundles the .NET runtime, no install needed).

.PARAMETER DoNotStart
  Register the service but do not start it.

.EXAMPLE
  .\install-service.ps1
  .\install-service.ps1 -SelfContained
  .\install-service.ps1 -InstallDir D:\CopyBot
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles "CopyBot"),
    [string]$ServiceName = "Windows Shadow Sync Service",
    [string]$DisplayName = "Windows Shadow Sync Service",
    [string]$Description = "Copies contents of attached removable drives to a configured backup folder.",
    [string]$ConfigPath = (Join-Path $PSScriptRoot "..\src\CopyBot\config.json"),
    [switch]$SelfContained,
    [switch]$DoNotStart
)

$ErrorActionPreference = "Stop"

# --- Elevation check -------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated (Administrator) PowerShell prompt."
}

# --- Locate project --------------------------------------------------------
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot "src\CopyBot"
$Csproj = Join-Path $ProjectDir "CopyBot.csproj"
if (-not (Test-Path $Csproj)) {
    throw "Project file not found: $Csproj"
}

# --- Publish ---------------------------------------------------------------
Write-Host "==> Publishing CopyBot to '$InstallDir'" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

$publishArgs = @(
    "publish", $Csproj,
    "-c", "Release",
    "--nologo",
    "-r", "win-x64",
    "-o", $InstallDir
)
if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
} else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# --- Deploy configuration --------------------------------------------------
if (-not (Test-Path $ConfigPath)) {
    $ConfigPath = Join-Path $ProjectDir "config.json"
}

if (Test-Path $ConfigPath) {
    $targetConfig = Join-Path $InstallDir "config.json"
    Copy-Item -Path $ConfigPath -Destination $targetConfig -Force
    Write-Host "==> Configuration copied to '$targetConfig'" -ForegroundColor Cyan
} else {
    Write-Warning "No config.json found at '$ConfigPath'; the service will use built-in defaults."
}

# --- Register the service --------------------------------------------------
$exe = Join-Path $InstallDir "CopyBot.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable not found: $exe"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "==> Stopping existing service '$ServiceName'" -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
    }
    Write-Host "==> Removing existing service '$ServiceName'" -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to delete existing service (exit $LASTEXITCODE)." }
    Start-Sleep -Seconds 2
}

Write-Host "==> Creating service '$ServiceName'" -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName $exe `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType Automatic | Out-Null

# Recovery options: restart on failure.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# Prefer a delayed auto start so the service does not slow down boot.
try {
    sc.exe config $ServiceName start= delayed-auto | Out-Null
} catch {
    # delayed-auto is not available on all Windows builds; Automatic is fine.
}

if (-not $DoNotStart) {
    Write-Host "==> Starting service '$ServiceName'" -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    $svc = Get-Service -Name $ServiceName
    Write-Host "==> Service '$ServiceName' status: $($svc.Status)" -ForegroundColor Green
} else {
    Write-Host "==> Service '$ServiceName' registered but NOT started (-DoNotStart)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "  Service name : $ServiceName"
Write-Host "  Executable   : $exe"
Write-Host "  Config       : $targetConfig"
Write-Host "  Backup root  : configure 'BackupRootFolder' in the config file."