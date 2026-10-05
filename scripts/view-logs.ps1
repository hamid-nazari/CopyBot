<#
.SYNOPSIS
  Shows the CopyBot service logs and opens Windows Event Viewer.

.DESCRIPTION
  Prints the most recent events logged by the CopyBot service (source "Windows Shadow
  Sync Service") from the Windows Application log, then opens Event Viewer so you can
  filter further.

.PARAMETER Source
  Event source to filter on. Default: Windows Shadow Sync Service.

.PARAMETER LogName
  Log to query. Default: Application.

.PARAMETER Count
  Maximum number of recent events to show. Default: 50.

.PARAMETER NoConsole
  Only open Event Viewer; do not print events to the console.

.PARAMETER NoViewer
  Only print events; do not open Event Viewer.

.EXAMPLE
  .\scripts\view-logs.ps1
  .\scripts\view-logs.ps1 -Count 100 -NoViewer
#>
[CmdletBinding()]
param(
    [string]$Source = "Windows Shadow Sync Service",
    [string]$LogName = "Application",
    [int]$Count = 50,
    [switch]$NoConsole,
    [switch]$NoViewer
)

$ErrorActionPreference = "Stop"

$xpath = "*[System[Provider[@Name='$Source']]]"
$events = @(Get-WinEvent -LogName $LogName -FilterXPath $xpath -MaxEvents $Count -ErrorAction SilentlyContinue)

if (-not $NoConsole) {
    if ($events.Count -eq 0) {
        Write-Host "No events found from source '$Source' in the '$LogName' log." -ForegroundColor Yellow
    } else {
        Write-Host "Showing the $($events.Count) most recent events from source '$Source' (log: $LogName):" -ForegroundColor Cyan
        Write-Host ("-" * 72) -ForegroundColor DarkGray
        $events | ForEach-Object {
            $level = switch ($_.LevelDisplayName) {
                'Error'       { 'ERR ' }
                'Warning'     { 'WARN' }
                'Information' { 'INFO' }
                default       { $_.LevelDisplayName }
            }
            Write-Host ("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f $_.TimeCreated, $level, $_.Message)
        }
    }
}

if (-not $NoViewer) {
    Start-Process -FilePath "eventvwr.msc"
    Write-Host ""
    Write-Host "Opened Windows Event Viewer. To filter on this service:" -ForegroundColor Green
    Write-Host "  Windows Logs -> Application -> Filter Current Log... -> 'Event sources: $Source'" -ForegroundColor Green
}