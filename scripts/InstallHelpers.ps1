function Get-ClockStartupPreference {
    param([string]$InstallPath, [string]$SettingsPath, [string]$ShortcutPath)

    $savedPreference = $null
    if (Test-Path -LiteralPath $SettingsPath) {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($SettingsPath)
        $node = $document.SelectSingleNode("/*[local-name()='FloatingClockSettings']/*[local-name()='StartWithWindows']")
        if ($null -ne $node) {
            $savedPreference = [System.Xml.XmlConvert]::ToBoolean($node.InnerText)
        }
    }

    if ($savedPreference -eq $false) { return $false }
    # Older versions did not immediately save this switch. A removed shortcut
    # must stay removed when upgrading an existing installation.
    if (Test-Path -LiteralPath $InstallPath) {
        return [bool](Test-Path -LiteralPath $ShortcutPath)
    }
    if ($null -ne $savedPreference) { return $savedPreference }
    return $true
}

function Set-ClockSavedStartupPreference {
    param([string]$SettingsPath, [bool]$Enabled)

    if (-not (Test-Path -LiteralPath $SettingsPath)) { return }
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $document.Load($SettingsPath)
    $node = $document.SelectSingleNode("/*[local-name()='FloatingClockSettings']/*[local-name()='StartWithWindows']")
    if ($null -eq $node) {
        $node = $document.CreateElement('StartWithWindows', $document.DocumentElement.NamespaceURI)
        $positionNode = $document.SelectSingleNode("/*[local-name()='FloatingClockSettings']/*[local-name()='PositionInPixels']")
        if ($null -ne $positionNode) { [void]$document.DocumentElement.InsertBefore($node, $positionNode) }
        else { [void]$document.DocumentElement.AppendChild($node) }
    }
    $value = [System.Xml.XmlConvert]::ToString($Enabled)
    if ($node.InnerText -eq $value) { return }
    $node.InnerText = $value
    $temporary = $SettingsPath + '.install.tmp'
    try {
        $document.Save($temporary)
        [System.IO.File]::Replace($temporary, $SettingsPath, $SettingsPath + '.install.bak')
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
