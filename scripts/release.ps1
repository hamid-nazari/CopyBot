<#
.SYNOPSIS
  Builds and publishes CopyBot, then creates the deployable release ZIP.

.DESCRIPTION
  Runs `dotnet build` and `dotnet publish`, then (unless -NoPackage) invokes
  scripts/package-zip.ps1 against the published output and finally deletes the
  publication folder (unless -KeepPublish).

.PARAMETER Configuration
  Build configuration. Default: Release.

.PARAMETER Runtime
  Runtime identifier. Default: win-x64.

.PARAMETER Project
  Path to CopyBot.csproj. Default: <repo>\src\CopyBot\CopyBot.csproj.

.PARAMETER PublishDir
  Folder the app is published to (and removed after packaging).
  Default: <repo>\release.

.PARAMETER OutputDir
  Folder the release ZIP is written to. Default: <repo>\dist.

.PARAMETER SelfContained
  Publish a self-contained build (bundles the .NET runtime).

.PARAMETER KeepSymbols
  Include *.pdb files in the ZIP (by default they are excluded).

.PARAMETER NoPackage
  Only build and publish; do not create the ZIP.

.PARAMETER KeepPublish
  Do not delete the published folder after packaging.

.EXAMPLE
  .\scripts\release.ps1
  .\scripts\release.ps1 -SelfContained -KeepPublish
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Project = (Join-Path $PSScriptRoot "..\src\CopyBot\CopyBot.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\release"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\dist"),
    [switch]$SelfContained,
    [switch]$KeepSymbols,
    [switch]$NoPackage,
    [switch]$KeepPublish
)

$ErrorActionPreference = "Stop"

$Project = [System.IO.Path]::GetFullPath($Project)
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

# --- Build ------------------------------------------------------------------
Write-Host "==> Building CopyBot ($Configuration)" -ForegroundColor Cyan
dotnet build $Project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

# --- Publish ----------------------------------------------------------------
Write-Host "==> Publishing CopyBot to '$PublishDir'" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$publishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "--nologo",
    "-r", $Runtime,
    "-o", $PublishDir,
    "--self-contained",
    $selfContainedValue
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# --- Package ----------------------------------------------------------------
if (-not $NoPackage) {
    $packageScript = Join-Path $PSScriptRoot "package-zip.ps1"
    Write-Host "==> Packaging release ZIP" -ForegroundColor Cyan

    $packageParams = @{
        Source = $PublishDir
        OutputDir = $OutputDir
    }
    if (-not $KeepSymbols) {
        $packageParams.ExcludePdb = $true
    }

    & $packageScript @packageParams
    if ($LASTEXITCODE -ne 0) {
        throw "package-zip.ps1 failed with exit code $LASTEXITCODE. The publish folder was kept for inspection."
    }
}

# --- Cleanup the publish folder ---------------------------------------------
if (-not $KeepPublish -and (Test-Path $PublishDir)) {
    Remove-Item -Path $PublishDir -Recurse -Force
    Write-Host "==> Removed publish folder: $PublishDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Release complete." -ForegroundColor Green