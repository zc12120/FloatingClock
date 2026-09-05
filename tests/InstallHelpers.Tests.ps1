$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\InstallHelpers.ps1')
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('FloatingClock-install-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$exe = Join-Path $testRoot 'FloatingClock.exe'
$settingsFile = Join-Path $testRoot 'settings.xml'
$shortcut = Join-Path $testRoot 'Floating Clock.lnk'

function Assert-Preference {
    param([bool]$Expected, [string]$Case)
    $actual = Get-ClockStartupPreference -InstallPath $exe -SettingsPath $settingsFile -ShortcutPath $shortcut
    if ($actual -ne $Expected) { throw "$Case : expected $Expected, got $actual" }
    Write-Output "PASS: $Case"
}

function Write-Settings {
    param([string]$Startup = 'true', [int]$Version = 11)
    $startupMember = if ($Startup -eq '') { '' } else { "<StartWithWindows>$Startup</StartWithWindows>" }
    [System.IO.File]::WriteAllText($settingsFile, "<FloatingClockSettings xmlns='http://schemas.datacontract.org/2004/07/FloatingClock'><Version>$Version</Version><Left>-800</Left><Top>200</Top><ThemeMode>4</ThemeMode>$startupMember<PositionInPixels>true</PositionInPixels></FloatingClockSettings>")
}

try {
    Assert-Preference $true 'Fresh install defaults to enabled'
    Write-Settings 'false'
    Assert-Preference $false 'Reinstall preserves saved opt-out'
    Write-Settings 'true'
    [System.IO.File]::WriteAllText($exe, 'fixture')
    Assert-Preference $false 'Legacy removed shortcut overrides stale enabled setting'
    Set-ClockSavedStartupPreference -SettingsPath $settingsFile -Enabled $false
    [xml]$saved = [System.IO.File]::ReadAllText($settingsFile)
    if ($saved.FloatingClockSettings.StartWithWindows -ne 'false' -or $saved.FloatingClockSettings.ThemeMode -ne '4' -or $saved.FloatingClockSettings.Left -ne '-800') {
        throw 'Updating startup changed unrelated settings or did not persist opt-out'
    }
    Write-Output 'PASS: Saved opt-out and unrelated settings survive upgrade'
    [System.IO.File]::WriteAllText($shortcut, 'fixture')
    Assert-Preference $false 'Saved opt-out overrides an accidentally recreated shortcut'
    Write-Settings 'true'
    Assert-Preference $true 'Enabled existing installation remains enabled'
    Write-Settings '' 9
    Set-ClockSavedStartupPreference -SettingsPath $settingsFile -Enabled $false
    [xml]$saved = [System.IO.File]::ReadAllText($settingsFile)
    if ($saved.FloatingClockSettings.StartWithWindows -ne 'false' -or $saved.FloatingClockSettings.Version -ne '9') {
        throw 'Missing startup member was not added without changing version'
    }
    Write-Output 'PASS: Missing legacy member added without changing other migrations'
    $order = @($saved.DocumentElement.ChildNodes | ForEach-Object { $_.LocalName })
    if ([Array]::IndexOf($order, 'StartWithWindows') -gt [Array]::IndexOf($order, 'PositionInPixels')) {
        throw 'Data contract member order was not preserved'
    }
    Write-Output 'PASS: Data contract member order preserved'
    [System.IO.File]::WriteAllText($settingsFile, '<invalid')
    $rejected = $false
    try { Get-ClockStartupPreference -InstallPath $exe -SettingsPath $settingsFile -ShortcutPath $shortcut | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid XML silently reset the startup preference' }
    Write-Output 'PASS: Invalid settings stop installation before mutation'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
