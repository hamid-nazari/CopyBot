<#
.SYNOPSIS
  Installs the CopyBot Windows service ("Windows Shadow Sync Service").

.DESCRIPTION
  Registers the CopyBot Windows service and starts it.

  When -FromSource is NOT supplied the script installs the service *in place*: the
  folder where CopyBot.exe is found becomes $InstallDir and the config.json next to it
  is used directly (no files are copied to a new location).

  When -FromSource IS supplied the project is published into $InstallDir and the config
  is deployed there (the original behaviour).

  Run from an elevated PowerShell prompt (Administrator).

.PARAMETER InstallDir
  Folder the service is installed from. With -FromSource this is where the project is
  published and the config is deployed. Default: %ProgramFiles%\CopyBot.
  WITHOUT -FromSource this is ignored: the service is installed in place at -BinaryRoot.

.PARAMETER ServiceName
  Name of the Windows service. Default: Windows Shadow Sync Service.

.PARAMETER DisplayName
  Human-friendly service display name.

.PARAMETER Description
  Description shown in Services.msc.

.PARAMETER BinaryRoot
  Folder containing an already-built CopyBot.exe. Used when -FromSource is NOT
  supplied, and it becomes $InstallDir. Default: the folder of this script
  ($PSScriptRoot).

.PARAMETER ConfigPath
  Optional config file source. Default: <BinaryRoot>\config.json (no-build mode), or
  <project>\config.json (with -FromSource).

.PARAMETER FromSource
  Build/publish CopyBot from source before installing. When omitted, an existing build
  from -BinaryRoot is used in place and nothing is compiled or copied.

.PARAMETER SelfContained
  (Only meaningful with -FromSource.) Publish a self-contained build that bundles the
  .NET runtime so no separate runtime install is required.

.PARAMETER DoNotStart
  Register the service but do not start it.

.EXAMPLE
  .\install-service.ps1                      # install in place from the build next to this script
  .\install-service.ps1 -BinaryRoot D:\CopyBot
  .\install-service.ps1 -FromSource          # build, publish to %ProgramFiles%\CopyBot, then install
  .\install-service.ps1 -FromSource -InstallDir D:\CopyBot
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles "CopyBot"),
    [string]$ServiceName = "Windows Shadow Sync Service",
    [string]$DisplayName = "Windows Shadow Sync Service",
    [string]$Description = "Copies contents of attached removable drives to a configured backup folder.",
    [string]$BinaryRoot = $PSScriptRoot,
    [string]$ConfigPath,
    [switch]$FromSource,
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

# --- Helper: locate the CopyBot project by walking up from a start path -----
function Find-CopyBotProjectDir {
    param([string]$StartPath)

    $current = [System.IO.Path]::GetFullPath($StartPath)
    while ($true) {
        $repoLayout = Join-Path $current "src\CopyBot\CopyBot.csproj"
        if (Test-Path $repoLayout) {
            return (Split-Path -Parent $repoLayout)
        }

        $direct = Join-Path $current "CopyBot.csproj"
        if (Test-Path $direct) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) { break }
        $current = $parent
    }

    return $null
}

# --- Resolve paths ----------------------------------------------------------
$BinaryRoot = [System.IO.Path]::GetFullPath($BinaryRoot)

if (-not $FromSource) {
    # Reuse an existing build IN PLACE. The folder that holds CopyBot.exe is the
    # install location and the config next to it is used directly (no copying).
    $InstallDir = $BinaryRoot
    $exe = Join-Path $InstallDir "CopyBot.exe"
    if (-not (Test-Path $exe)) {
        throw "CopyBot.exe was not found in '$InstallDir'. Pass -BinaryRoot pointing to a built CopyBot, or use -FromSource to build it from source."
    }

    Write-Host "==> Installing existing build in place: '$exe'" -ForegroundColor Cyan

    if (-not $ConfigPath) {
        $ConfigPath = Join-Path $InstallDir "config.json"
    }
    if ($SelfContained) {
        Write-Warning "-SelfContained is ignored unless -FromSource is used."
    }
}
else {
    # Build and publish from source (original behaviour).
    $InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

    $ProjectDir = Find-CopyBotProjectDir -StartPath $PSScriptRoot
    if (-not $ProjectDir) {
        throw "Could not locate the CopyBot project (CopyBot.csproj) from '$PSScriptRoot'. Run -FromSource from the repository 'scripts' folder."
    }

    $Csproj = Join-Path $ProjectDir "CopyBot.csproj"
    if (-not (Test-Path $Csproj)) {
        throw "Project file not found: $Csproj"
    }

    if (-not $ConfigPath) {
        $ConfigPath = Join-Path $ProjectDir "config.json"
    }

    Write-Host "==> Building and publishing CopyBot to '$InstallDir'" -ForegroundColor Cyan
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

    $exe = Join-Path $InstallDir "CopyBot.exe"
    if (-not (Test-Path $exe)) {
        throw "Published executable not found: $exe"
    }
}

# --- Configuration handling -------------------------------------------------
$targetConfig = Join-Path $InstallDir "config.json"

if ($FromSource) {
    # Deploy the selected config into the (possibly new) install folder.
    if (Test-Path $ConfigPath) {
        Copy-Item -Path $ConfigPath -Destination $targetConfig -Force
        Write-Host "==> Configuration copied to '$targetConfig'" -ForegroundColor Cyan
    } else {
        Write-Warning "No config.json found at '$ConfigPath'; the service will use built-in defaults."
    }
} else {
    # In-place install: keep and use the config that already sits next to the exe.
    if (Test-Path $ConfigPath) {
        Write-Host "==> Using in-place configuration: '$targetConfig'" -ForegroundColor Cyan
    } else {
        Write-Warning "No config.json found next to the executable at '$targetConfig'; the service will use built-in defaults (or '%ProgramData%\CopyBot\config.json')."
    }
}

# --- Register the service --------------------------------------------------
if (-not (Test-Path $exe)) {
    throw "Service executable not found: $exe"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "==> Stopping existing service '$ServiceName'" -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
    }
    Write-Host "==> Removing existing service '$ServiceName'" -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete existing service (exit $LASTEXITCODE)."
    }
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