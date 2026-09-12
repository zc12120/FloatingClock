function Read-ClockSettingsDocument {
    param([string]$SettingsPath)

    $namespace = 'http://schemas.datacontract.org/2004/07/FloatingClock'
    $members = @('Version', 'Left', 'Top', 'ShowDate', 'ShowSeconds', 'Use24Hour', 'AlwaysOnTop', 'Locked', 'ClickThrough', 'ThemeMode', 'ScaleMode', 'SurfaceOpacity', 'SurfaceTone', 'FontMode', 'DockAnchor', 'StartWithWindows', 'PositionInPixels')
    $booleans = @('ShowDate', 'ShowSeconds', 'Use24Hour', 'AlwaysOnTop', 'Locked', 'ClickThrough', 'StartWithWindows', 'PositionInPixels')
    $doubles = @('Left', 'Top', 'SurfaceOpacity')
    $options = New-Object System.Xml.XmlReaderSettings
    $options.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $options.XmlResolver = $null
    $options.MaxCharactersInDocument = 65536
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($SettingsPath, $options)
    try { $document.Load($reader) }
    finally { $reader.Dispose() }
    $root = $document.DocumentElement
    if ($null -eq $root -or $root.LocalName -ne 'FloatingClockSettings' -or $root.NamespaceURI -ne $namespace) {
        throw 'Invalid settings root or namespace; installation has not changed the settings.'
    }
    $lastIndex = -1
    foreach ($node in $root.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $index = [Array]::IndexOf($members, $node.LocalName)
        if ($index -lt 0) { continue }
        if ($node.NamespaceURI -ne $namespace -or $index -le $lastIndex) {
            throw "Duplicate, out-of-order or invalid settings member: $($node.LocalName)"
        }
        $lastIndex = $index
        if ($null -ne $node.SelectSingleNode('*')) { throw "Nested settings member: $($node.LocalName)" }
        if ($booleans -contains $node.LocalName) { [void][System.Xml.XmlConvert]::ToBoolean($node.InnerText) }
        elseif ($doubles -contains $node.LocalName) { [void][System.Xml.XmlConvert]::ToDouble($node.InnerText) }
        else {
            $value = [System.Xml.XmlConvert]::ToInt32($node.InnerText)
            if ($node.LocalName -eq 'Version' -and ($value -gt 11 -or $value -lt 0)) {
                throw "Unsupported settings version: $value; refusing to downgrade."
            }
        }
    }
    return ,$document
}

function Get-ClockStartupPreference {
    param([string]$InstallPath, [string]$SettingsPath, [string]$ShortcutPath)

    $savedPreference = $null
    if (Test-Path -LiteralPath $SettingsPath) {
        $document = Read-ClockSettingsDocument -SettingsPath $SettingsPath
        $node = $document.DocumentElement.SelectSingleNode("*[local-name()='StartWithWindows']")
        if ($null -ne $node) { $savedPreference = [System.Xml.XmlConvert]::ToBoolean($node.InnerText) }
    }
    if ($savedPreference -eq $false) { return $false }
    # Older versions did not immediately save this switch. A removed shortcut
    # must stay removed when upgrading an existing installation.
    if (Test-Path -LiteralPath $InstallPath) { return [bool](Test-Path -LiteralPath $ShortcutPath) }
    if ($null -ne $savedPreference) { return $savedPreference }
    return $true
}

function Set-ClockSavedStartupPreference {
    param([string]$SettingsPath, [bool]$Enabled)

    $existed = Test-Path -LiteralPath $SettingsPath
    $previous = $null
    if ($existed) {
        $previous = [System.IO.File]::ReadAllText($SettingsPath)
        $document = Read-ClockSettingsDocument -SettingsPath $SettingsPath
    }
    else {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.LoadXml('<FloatingClockSettings xmlns="http://schemas.datacontract.org/2004/07/FloatingClock"><Version>11</Version><Left>NaN</Left><Top>NaN</Top><ShowDate>true</ShowDate><ShowSeconds>false</ShowSeconds><Use24Hour>true</Use24Hour><AlwaysOnTop>true</AlwaysOnTop><Locked>false</Locked><ClickThrough>false</ClickThrough><ThemeMode>2</ThemeMode><ScaleMode>2</ScaleMode><SurfaceOpacity>0.85</SurfaceOpacity><SurfaceTone>2</SurfaceTone><FontMode>4</FontMode><DockAnchor>2</DockAnchor><StartWithWindows>true</StartWithWindows><PositionInPixels>true</PositionInPixels></FloatingClockSettings>')
    }
    $node = $document.DocumentElement.SelectSingleNode("*[local-name()='StartWithWindows']")
    if ($null -eq $node) {
        $node = $document.CreateElement('StartWithWindows', $document.DocumentElement.NamespaceURI)
        $positionNode = $document.DocumentElement.SelectSingleNode("*[local-name()='PositionInPixels']")
        if ($null -ne $positionNode) { [void]$document.DocumentElement.InsertBefore($node, $positionNode) }
        else { [void]$document.DocumentElement.AppendChild($node) }
    }
    $value = [System.Xml.XmlConvert]::ToString($Enabled)
    if ($existed -and $node.InnerText -eq $value) { return }
    $node.InnerText = $value
    $SettingsPath = [System.IO.Path]::GetFullPath($SettingsPath)
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($SettingsPath))
    $temporary = $SettingsPath + '.' + [Guid]::NewGuid().ToString('N') + '.install.tmp'
    $writerSettings = New-Object System.Xml.XmlWriterSettings
    $writerSettings.Indent = $true
    $writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
    try {
        $writer = [System.Xml.XmlWriter]::Create($temporary, $writerSettings)
        try { $document.Save($writer) }
        finally { $writer.Dispose() }
        if ($existed) {
            if ([System.IO.File]::ReadAllText($SettingsPath) -cne $previous) {
                throw 'Settings changed in another process; refusing to overwrite them.'
            }
            [System.IO.File]::Replace($temporary, $SettingsPath, $SettingsPath + '.install.bak')
        }
        else { [System.IO.File]::Move($temporary, $SettingsPath) }
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
