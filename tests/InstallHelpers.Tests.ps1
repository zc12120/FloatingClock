[CmdletBinding()]
param([string]$ReportPath)

$ErrorActionPreference = 'Stop'
$testReport = New-Object 'System.Collections.Generic.List[string]'
function Write-TestResult {
    param([string]$Message)
    $testReport.Add($Message)
    Write-Output $Message
}
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
    Write-TestResult "PASS: $Case"
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
    Write-TestResult 'PASS: Saved opt-out and unrelated settings survive upgrade'
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
    Write-TestResult 'PASS: Missing legacy member added without changing other migrations'
    $order = @($saved.DocumentElement.ChildNodes | ForEach-Object { $_.LocalName })
    if ([Array]::IndexOf($order, 'StartWithWindows') -gt [Array]::IndexOf($order, 'PositionInPixels')) {
        throw 'Data contract member order was not preserved'
    }
    Write-TestResult 'PASS: Data contract member order preserved'
    [System.IO.File]::WriteAllText($settingsFile, '<invalid')
    $rejected = $false
    try { Get-ClockStartupPreference -InstallPath $exe -SettingsPath $settingsFile -ShortcutPath $shortcut | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid XML silently reset the startup preference' }
    Write-TestResult 'PASS: Invalid settings stop installation before mutation'

    $missing = Join-Path $testRoot 'missing-settings.xml'
    Set-ClockSavedStartupPreference -SettingsPath $missing -Enabled $false
    $created = Read-ClockSettingsDocument -SettingsPath $missing
    if ($created.FloatingClockSettings.StartWithWindows -ne 'false' -or $created.FloatingClockSettings.ShowDate -ne 'true' -or $created.FloatingClockSettings.ScaleMode -ne '2' -or $created.FloatingClockSettings.Left -ne 'NaN') {
        throw 'Missing settings did not preserve opt-out with complete defaults'
    }
    Write-TestResult 'PASS: Missing settings persist opt-out with complete defaults'
    $fresh = Join-Path $testRoot 'fresh-settings.xml'
    Set-ClockSavedStartupPreference -SettingsPath $fresh -Enabled $true
    if (-not (Test-Path -LiteralPath $fresh)) { throw 'Fresh enabled settings were not created' }
    Set-ClockSavedStartupPreference -SettingsPath $fresh -Enabled $false
    if (-not (Test-Path -LiteralPath ($fresh + '.install.bak'))) { throw 'Atomic update did not retain backup' }
    Write-TestResult 'PASS: Fresh defaults and atomic backup are preserved'

    $prefix = '<FloatingClockSettings xmlns="http://schemas.datacontract.org/2004/07/FloatingClock">'
    $suffix = '</FloatingClockSettings>'
    $invalidCases = @(
        '<FloatingClockSettings><StartWithWindows>false</StartWithWindows></FloatingClockSettings>',
        '<FloatingClockSettings xmlns="wrong"><StartWithWindows>false</StartWithWindows></FloatingClockSettings>',
        ($prefix + '<Version>11</Version><ScaleMode>bad</ScaleMode><StartWithWindows>false</StartWithWindows>' + $suffix),
        ($prefix + '<StartWithWindows>false</StartWithWindows><StartWithWindows>false</StartWithWindows>' + $suffix),
        ($prefix + '<PositionInPixels>true</PositionInPixels><StartWithWindows>false</StartWithWindows>' + $suffix),
        ($prefix + '<Version>12</Version><StartWithWindows>false</StartWithWindows>' + $suffix),
        ('<!DOCTYPE FloatingClockSettings [<!ENTITY value "false">]>' + $prefix + '<StartWithWindows>&value;</StartWithWindows>' + $suffix),
        ($prefix + '<ShowDate><value>true</value></ShowDate>' + $suffix),
        ($prefix + '<!--' + ('x' * 65536) + '-->' + $suffix)
    )
    $caseNumber = 0
    foreach ($invalidXml in $invalidCases) {
        $caseNumber++
        [System.IO.File]::WriteAllText($settingsFile, $invalidXml)
        $rejectedRead = $false
        $rejectedWrite = $false
        try { Get-ClockStartupPreference -InstallPath $exe -SettingsPath $settingsFile -ShortcutPath $shortcut | Out-Null }
        catch { $rejectedRead = $true }
        try { Set-ClockSavedStartupPreference -SettingsPath $settingsFile -Enabled $false }
        catch { $rejectedWrite = $true }
        if (-not $rejectedRead -or -not $rejectedWrite -or [System.IO.File]::ReadAllText($settingsFile) -ne $invalidXml) {
            throw "Unsafe settings accepted or changed: case $caseNumber"
        }
        Write-TestResult "PASS: Invalid or incompatible configuration $caseNumber rejected without mutation"
    }
}
catch {
    $testReport.Add('FAIL: ' + $_.Exception.Message)
    throw
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
    if ($ReportPath) { [System.IO.File]::WriteAllLines($ReportPath, $testReport.ToArray()) }
}
