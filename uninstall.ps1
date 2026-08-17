[CmdletBinding()]
param([switch]$RemoveSettings)

$ErrorActionPreference = 'Stop'
$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FloatingClock'
$installPath = Join-Path $installRoot 'FloatingClock.exe'
$startupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Floating Clock.lnk'
$desktopPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Floating Clock.lnk'

Get-Process -Name 'FloatingClock' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path $startupPath -Force -ErrorAction SilentlyContinue
Remove-Item -Path $desktopPath -Force -ErrorAction SilentlyContinue
Remove-Item -Path $installPath -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $installRoot '*.ttf') -Force -ErrorAction SilentlyContinue

if ($RemoveSettings) {
    Remove-Item -Path $installRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Floating Clock was removed.'
