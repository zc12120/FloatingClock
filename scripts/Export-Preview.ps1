[CmdletBinding()]
param(
    [string]$OutputDirectory
)

# Render the real WPF clock and WinForms menus without starting the application,
# showing windows, touching user settings/startup entries, or moving the cursor.
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\preview' }
$assemblyPath = Join-Path $projectRoot 'artifacts\FloatingClock.exe'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing, System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$members = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$settingsType = $assembly.GetType('FloatingClock.ClockSettings', $true)
$windowType = $assembly.GetType('FloatingClock.ClockWindow', $true)
$presetType = $assembly.GetType('FloatingClock.ClockThemePresets', $true)
$sampleTime = [DateTime]::new(2026, 10, 7, 21, 48, 36)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Save-Clock {
    param($Window, [string]$Name, [double]$Dpi = 192)
    [void]$windowType.GetMethod('UpdateClock', $members).Invoke($Window, [object[]]@($sampleTime, $true))
    $visual = $windowType.GetField('scaler', $members).GetValue($Window)
    $size = [System.Windows.Size]::new($Window.Width, $Window.Height)
    $visual.Measure($size)
    $visual.Arrange([System.Windows.Rect]::new([System.Windows.Point]::new(0, 0), $size))
    $visual.UpdateLayout()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        [int][Math]::Ceiling($size.Width * $Dpi / 96), [int][Math]::Ceiling($size.Height * $Dpi / 96),
        $Dpi, $Dpi, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory $Name))
    try { $encoder.Save($stream) }
    finally { $stream.Dispose() }
}

function Save-Menu {
    param([System.Windows.Forms.ToolStrip]$Menu, [string]$Name)
    $Menu.PerformLayout()
    $Menu.Size = $Menu.GetPreferredSize([System.Drawing.Size]::Empty)
    $Menu.CreateControl()
    $bitmap = [System.Drawing.Bitmap]::new($Menu.Width, $Menu.Height)
    try {
        $Menu.DrawToBitmap($bitmap, [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height))
        # WM_PRINT on a hidden ToolStrip omits hosted controls. Paint those same
        # controls into their layout bounds to complete the offscreen snapshot.
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            foreach ($item in $Menu.Items) {
                if ($item -isnot [System.Windows.Forms.ToolStripControlHost]) { continue }
                $control = $item.Control
                $child = [System.Drawing.Bitmap]::new($control.Width, $control.Height)
                try {
                    $control.DrawToBitmap($child, [System.Drawing.Rectangle]::new(0, 0, $child.Width, $child.Height))
                    $graphics.DrawImageUnscaled($child, $control.Left, $control.Top)
                }
                finally { $child.Dispose() }
            }
        }
        finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $OutputDirectory $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

$settings = $settingsType.GetMethod('CreateDefault', $static).Invoke($null, @())
$arguments = [object[]]@($settings, [Action]{}, [Action[bool]]{ param($value) },
    [Func[bool]]{ return $false }, [Action[bool]]{ param($value) }, [Action]{}, [Action]{})
$window = [Activator]::CreateInstance($windowType, $arguments)
try {
    $names = @('reload', 'dark-hour', 'moonlight', 'smtvv')
    for ($index = 0; $index -lt $names.Count; $index++) {
        $window.SetThemePreset($index)
        Save-Clock -Window $window -Name ('clock-' + $names[$index] + '.png')
    }
    Save-Clock -Window $window -Name 'clock-smtvv-100.png' -Dpi 96
    Save-Clock -Window $window -Name 'clock-smtvv-150.png' -Dpi 144
    $menu = $window.SettingsMenu
    $theme = $menu.Items.Find('theme', $true)[0]
    $theme.Select()
    Save-Menu -Menu $menu -Name 'menu-smtvv.png'
    $theme.DropDownItems[3].Select()
    Save-Menu -Menu $theme.DropDown -Name 'menu-smtvv-themes.png'
    $window.SetThemePreset(0)
    Save-Clock -Window $window -Name 'clock-100.png' -Dpi 96
    Save-Clock -Window $window -Name 'clock-150.png' -Dpi 144
    $theme.Select()
    Save-Menu -Menu $menu -Name 'menu-main.png'
    $theme.DropDownItems[1].Select()
    Save-Menu -Menu $theme.DropDown -Name 'menu-themes.png'
    $font = $menu.Items.Find('font', $true)[0]
    $font.DropDownItems[4].Select()
    Save-Menu -Menu $font.DropDown -Name 'menu-fonts.png'
    $window.ToggleShowDate()
    $window.ToggleShowSeconds()
    $window.ToggleUse24Hour()
    Save-Clock -Window $window -Name 'clock-compact-12h.png'
    $window.SetFontMode(5)
    Save-Clock -Window $window -Name 'clock-wide-font.png'
    $window.SetThemePreset(2)
    Save-Menu -Menu $menu -Name 'menu-moonlight.png'
    $window.SetThemePreset(3)
    Save-Clock -Window $window -Name 'clock-smtvv-compact-12h.png'
    $window.SetScaleMode(0)
    Save-Clock -Window $window -Name 'clock-smtvv-mini-12h.png' -Dpi 96
    $window.SetScaleMode(5)
    Save-Clock -Window $window -Name 'clock-smtvv-large-12h.png'
    Write-Host "Preview images: $OutputDirectory"
}
finally {
    $window.PrepareForExit()
    $window.Close()
}
