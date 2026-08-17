[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src'
$artifactRoot = Join-Path $projectRoot 'artifacts'
$manifestPath = Join-Path $projectRoot 'app.manifest'
$iconPath = Join-Path $artifactRoot 'FloatingClock.ico'
$outputPath = Join-Path $artifactRoot 'FloatingClock.exe'

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

function New-ClockIcon {
    param([string]$Path)

    Add-Type -AssemblyName System.Drawing
    if (-not ('FloatingClockNativeIcon' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FloatingClockNativeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@
    }

    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $faceBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 5, 16, 9))
    $rimPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(170, 54, 217, 101)), 2
    $tickPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(210, 36, 168, 79)), 2
    $hourPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 76, 255, 120)), 4
    $minutePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 76, 255, 120)), 3
    $centerBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 255, 120))
    $hourPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hourPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $minutePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $minutePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $graphics.FillEllipse($faceBrush, 3, 3, 58, 58)
    $graphics.DrawEllipse($rimPen, 4, 4, 56, 56)

    for ($index = 0; $index -lt 12; $index++) {
        $angle = (($index * 30) - 90) * [Math]::PI / 180
        $outerX = 32 + ([Math]::Cos($angle) * 23)
        $outerY = 32 + ([Math]::Sin($angle) * 23)
        $innerX = 32 + ([Math]::Cos($angle) * 19)
        $innerY = 32 + ([Math]::Sin($angle) * 19)
        $graphics.DrawLine($tickPen, [float]$innerX, [float]$innerY, [float]$outerX, [float]$outerY)
    }

    $graphics.DrawLine($hourPen, 32, 32, 24, 24)
    $graphics.DrawLine($minutePen, 32, 32, 43, 19)
    $graphics.FillEllipse($centerBrush, 28.5, 28.5, 7, 7)

    $handle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($Path)
    try {
        $icon.Save($stream)
    }
    finally {
        $stream.Dispose()
        $icon.Dispose()
        [FloatingClockNativeIcon]::DestroyIcon($handle) | Out-Null
        $centerBrush.Dispose()
        $minutePen.Dispose()
        $hourPen.Dispose()
        $tickPen.Dispose()
        $rimPen.Dispose()
        $faceBrush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-ClockIcon -Path $iconPath

$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}

$compiler = Join-Path $frameworkRoot 'csc.exe'
$wpfRoot = Join-Path $frameworkRoot 'WPF'
$references = @(
    (Join-Path $wpfRoot 'PresentationCore.dll'),
    (Join-Path $wpfRoot 'PresentationFramework.dll'),
    (Join-Path $wpfRoot 'WindowsBase.dll'),
    (Join-Path $frameworkRoot 'System.Xaml.dll'),
    (Join-Path $frameworkRoot 'System.Drawing.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll'),
    (Join-Path $frameworkRoot 'System.Runtime.Serialization.dll'),
    (Join-Path $frameworkRoot 'System.Xml.dll')
)

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "Missing framework reference: $reference"
    }
}

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/utf8output',
    '/codepage:65001',
    '/main:FloatingClock.Program',
    "/out:$outputPath",
    "/win32icon:$iconPath",
    "/win32manifest:$manifestPath"
)
$compilerArguments += $references | ForEach-Object { "/reference:$_" }
$compilerArguments += Get-ChildItem -Path $sourceRoot -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName

& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler exited with code $LASTEXITCODE"
}

$testProcess = Start-Process -FilePath $outputPath -ArgumentList '--self-test' -Wait -PassThru
if ($testProcess.ExitCode -ne 0) {
    throw "Self-test failed with code $($testProcess.ExitCode)"
}

Write-Host "Build passed: $outputPath"

$fontRoot = Join-Path $projectRoot 'fonts'
if (Test-Path $fontRoot) {
    Get-ChildItem -Path $fontRoot -Filter '*.ttf' | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $artifactRoot -Force
    }
}

if ($Install) {
    $installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FloatingClock'
    $installPath = Join-Path $installRoot 'FloatingClock.exe'
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null

    Get-Process -Name 'FloatingClock' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 250
    Copy-Item -Path $outputPath -Destination $installPath -Force
    if (Test-Path $fontRoot) {
        Get-ChildItem -Path $fontRoot -Filter '*.ttf' | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $installRoot -Force
        }
    }

    $shell = New-Object -ComObject WScript.Shell
    try {
        $startupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Floating Clock.lnk'
        $desktopPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Floating Clock.lnk'

        foreach ($shortcutPath in @($startupPath, $desktopPath)) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $installPath
            $shortcut.WorkingDirectory = $installRoot
            $shortcut.Description = 'Floating Clock'
            $shortcut.IconLocation = "$installPath,0"
            $shortcut.Save()
        }
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }

    if (-not $NoLaunch) {
        Start-Process -FilePath $installPath
    }

    Write-Host "Installed: $installPath"
}
