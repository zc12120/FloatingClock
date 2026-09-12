using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    internal sealed class ClockWindow : Window
    {
        private readonly ClockSettings settings;
        private readonly Action persistSettings;
        private readonly Action<bool> clickThroughChanged;
        private readonly Func<bool> getStartupEnabled;
        private readonly Action<bool> setStartupEnabled;
        private readonly Action hideRequested;
        private readonly Action exitRequested;

        private readonly Grid designCanvas;
        private readonly ColumnDefinition yearColumn;
        private readonly ColumnDefinition leftDivColumn;
        private readonly ColumnDefinition timeColumn;
        private readonly ColumnDefinition rightDivColumn;
        private readonly ColumnDefinition dateColumn;
        private readonly Border terminalSurface;
        private readonly Border leftDivider;
        private readonly Border rightDivider;
        private readonly Border outline;
        private readonly ReloadClockChrome reloadChrome;
        private readonly Viewbox scaler;
        private readonly TextBlock timeText;
        private readonly Viewbox timeScaler;
        private readonly TextBlock yearText;
        private readonly TextBlock monthText;
        private readonly TextBlock dayText;
        private readonly Run hourRun;
        private readonly Run colonRun;
        private readonly Run minuteRun;
        private readonly Run secondsRun;
        private readonly Run periodRun;
        private readonly ClockMenu clockMenu;
        private readonly DispatcherTimer clockTimer;

        private ClockPalette palette;
        private LayeredSurface layeredSurface;
        private bool allowClose;
        private bool hotKeyRegistered;
        private bool presentQueued;
        private bool movedDuringDrag;
        private bool displayUpdateQueued;
        private bool displayUpdatePending;
        private DateTime lastClockSample;
        private double surfaceLeft;
        private double surfaceTop;
        private double lastLayoutWidth;
        private double lastLayoutHeight;
        private string lastPresentedFace;
        private IntPtr windowHandle;
        private HwndSource windowSource;
        private int lastDateStamp;

        public ClockWindow(
            ClockSettings settings,
            Action persistSettings,
            Action<bool> clickThroughChanged,
            Func<bool> getStartupEnabled,
            Action<bool> setStartupEnabled,
            Action hideRequested,
            Action exitRequested)
        {
            this.settings = settings;
            CanSaveSettings = true;
            this.persistSettings = persistSettings;
            this.clickThroughChanged = clickThroughChanged;
            this.getStartupEnabled = getStartupEnabled;
            this.setStartupEnabled = setStartupEnabled;
            this.hideRequested = hideRequested;
            this.exitRequested = exitRequested;
            settings.MigratePosition(DisplayGeometry.PrimaryScale);

            Title = "FLOAT CLOCK";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = Brushes.Black;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            FocusVisualStyle = null;
            Topmost = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            designCanvas = new Grid
            {
                Height = ClockLayout.DesignHeight,
                Background = Brushes.Transparent
            };

            Grid terminalGrid = new Grid();
            yearColumn = new ColumnDefinition();
            leftDivColumn = new ColumnDefinition();
            timeColumn = new ColumnDefinition();
            rightDivColumn = new ColumnDefinition();
            dateColumn = new ColumnDefinition();
            terminalGrid.ColumnDefinitions.Add(yearColumn);
            terminalGrid.ColumnDefinitions.Add(leftDivColumn);
            terminalGrid.ColumnDefinitions.Add(timeColumn);
            terminalGrid.ColumnDefinitions.Add(rightDivColumn);
            terminalGrid.ColumnDefinitions.Add(dateColumn);

            yearText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            TextOptions.SetTextFormattingMode(yearText, TextFormattingMode.Display);
            AutomationProperties.SetName(yearText, "Year");
            Grid.SetColumn(yearText, 0);
            terminalGrid.Children.Add(yearText);

            leftDivider = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 19, 0, 17),
                Opacity = 0.6,
                RenderTransform = new SkewTransform(-12, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(leftDivider, 1);
            terminalGrid.Children.Add(leftDivider);

            timeText = new TextBlock
            {
                LineHeight = 38,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(timeText, TextFormattingMode.Display);
            AutomationProperties.SetName(timeText, "Current time");

            hourRun = new Run();
            colonRun = new Run(":") { FontWeight = FontWeights.Light };
            minuteRun = new Run();
            secondsRun = new Run { FontSize = 12, FontWeight = FontWeights.Normal, BaselineAlignment = BaselineAlignment.Center };
            periodRun = new Run { FontSize = 9, FontWeight = FontWeights.Normal, BaselineAlignment = BaselineAlignment.Center };
            timeText.Inlines.Add(hourRun);
            timeText.Inlines.Add(colonRun);
            timeText.Inlines.Add(minuteRun);
            timeText.Inlines.Add(secondsRun);
            timeText.Inlines.Add(periodRun);

            timeScaler = new Viewbox
            {
                Child = timeText,
                Height = 38,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeScaler, 2);
            terminalGrid.Children.Add(timeScaler);

            rightDivider = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 19, 0, 17),
                Opacity = 0.6,
                RenderTransform = new SkewTransform(-12, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(rightDivider, 3);
            terminalGrid.Children.Add(rightDivider);

            StackPanel dateStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            monthText = CreateDatePart("Month");
            dayText = CreateDatePart("Day");
            dateStack.Children.Add(monthText);
            dateStack.Children.Add(dayText);
            Grid.SetColumn(dateStack, 4);
            terminalGrid.Children.Add(dateStack);

            terminalSurface = new Border
            {
                Height = ClockLayout.DesignHeight,
                Child = terminalGrid,
                IsHitTestVisible = false
            };
            AutomationProperties.SetName(terminalSurface, "Reload clock");
            designCanvas.Children.Add(terminalSurface);

            outline = new Border
            {
                Height = ClockLayout.DesignHeight,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };
            designCanvas.Children.Add(outline);
            reloadChrome = new ReloadClockChrome();
            designCanvas.Children.Add(reloadChrome);

            scaler = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Child = designCanvas
            };
            Content = scaler;

            clockMenu = new ClockMenu(settings, this, getStartupEnabled, setStartupEnabled, hideRequested, exitRequested);
            SizeChanged += HandleHostSizeChanged;
            IsVisibleChanged += HandleVisibleChanged;
            SourceInitialized += HandleSourceInitialized;

            clockTimer = new DispatcherTimer(DispatcherPriority.Background);
            clockTimer.Tick += HandleClockTick;

            ApplyFont();
            ApplyLayout(false);
            ApplyTheme();
            ApplyOpacity();
            ApplyInteractionState();

            if (settings.HasPosition)
            {
                surfaceLeft = settings.Left;
                surfaceTop = settings.Top;
            }
            if (settings.DockAnchor != 0 || !settings.HasPosition)
            {
                ApplyDock(false);
            }

            UpdateClock(DateTime.Now, true);
        }

        public bool CanSaveSettings { get; set; }

        public bool ClickThrough
        {
            get { return settings.ClickThrough; }
        }

        public bool IsHotKeyRegistered
        {
            get { return hotKeyRegistered; }
        }

        private double SurfaceScale
        {
            get { return layeredSurface == null ? DisplayGeometry.ScaleAt(surfaceLeft, surfaceTop) : layeredSurface.DpiScale; }
        }

        private double SurfaceWidth { get { return Width * SurfaceScale; } }
        private double SurfaceHeight { get { return Height * SurfaceScale; } }

        public void PrepareForExit()
        {
            if (allowClose) return;
            allowClose = true;
            displayUpdatePending = false;
            if (movedDuringDrag)
            {
                settings.DockAnchor = 0;
                movedDuringDrag = false;
            }
            // 退出只记录最后实际拖动位置，不再应用排队的停靠或限位。
            RememberPosition();
            clockTimer.Stop();
        }

        public void ToggleClickThrough()
        {
            SetClickThrough(!settings.ClickThrough);
        }

        public void DisableClickThrough()
        {
            SetClickThrough(false);
        }

        public void ToggleShowDate()
        {
            settings.ShowDate = !settings.ShowDate;
            RelayoutPreservingCenter();
            SaveAndRefresh();
        }

        public void ToggleShowSeconds()
        {
            settings.ShowSeconds = !settings.ShowSeconds;
            RelayoutPreservingCenter();
            UpdateClock(DateTime.Now, true);
            ScheduleNextTick();
            SaveAndRefresh();
        }

        public void ToggleUse24Hour()
        {
            settings.Use24Hour = !settings.Use24Hour;
            RelayoutPreservingCenter();
            UpdateClock(DateTime.Now, true);
            SaveAndRefresh();
        }

        public void SetThemeMode(int themeMode)
        {
            if (settings.ThemeMode == themeMode)
            {
                return;
            }

            settings.ThemeMode = themeMode;
            ApplyTheme();
            SaveAndRefresh();
        }

        public void SetThemePreset(int index)
        {
            if (!ClockThemePresets.Apply(settings, index)) return;
            ApplyFont();
            ApplyTheme();
            SaveAndRefresh();
        }

        public void SetSurfaceTone(int surfaceTone)
        {
            if (settings.SurfaceTone == surfaceTone)
            {
                return;
            }

            settings.SurfaceTone = surfaceTone;
            ApplyTheme();
            SaveAndRefresh();
        }

        public void SetFontMode(int fontMode)
        {
            if (settings.FontMode == fontMode)
            {
                return;
            }

            settings.FontMode = fontMode;
            ApplyFont();
            RefreshChrome();
            SaveAndRefresh();
        }

        public void SetScaleMode(int scaleMode)
        {
            if (settings.ScaleMode == scaleMode)
            {
                return;
            }

            double centerX = surfaceLeft + (SurfaceWidth / 2.0);
            double centerY = surfaceTop + (SurfaceHeight / 2.0);
            settings.ScaleMode = scaleMode;
            ApplyLayout(true);
            surfaceLeft = centerX - (SurfaceWidth / 2.0);
            surfaceTop = centerY - (SurfaceHeight / 2.0);
            if (settings.DockAnchor == 0) ClampToVisibleArea();
            else ApplyDock(false);
            RequestPresent();
            SaveAndRefresh();
        }

        public void SetSurfaceOpacity(double opacity)
        {
            double normalized = OpacityPresets.Normalize(opacity);
            if (Math.Abs(settings.SurfaceOpacity - normalized) < 0.001)
            {
                return;
            }

            settings.SurfaceOpacity = normalized;
            ApplyOpacity();
            SaveAndRefresh();
        }

        public void ToggleAlwaysOnTop()
        {
            settings.AlwaysOnTop = !settings.AlwaysOnTop;
            Topmost = false;
            SetSurfacesTopmost(settings.AlwaysOnTop);

            SaveAndRefresh();
        }

        public void ToggleLocked()
        {
            settings.Locked = !settings.Locked;
            ApplyInteractionState();
            SaveAndRefresh();
        }

        public void HandleDisplayChanged()
        {
            if (allowClose || Dispatcher.HasShutdownStarted) return;
            displayUpdatePending = true;
            if (displayUpdateQueued || (layeredSurface != null && layeredSurface.IsDragging)) return;
            displayUpdateQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                displayUpdateQueued = false;
                ApplyPendingDisplayUpdate();
            }));
        }

        private void ApplyPendingDisplayUpdate()
        {
            if (allowClose || !displayUpdatePending || (layeredSurface != null && layeredSurface.IsDragging)) return;
            displayUpdatePending = false;
            ApplyExtendedStyles();
            if (settings.DockAnchor == 0) ClampToVisibleArea();
            else ApplyDock(false);
            persistSettings();
            RequestPresent();
        }

        public void RefreshCurrentTime()
        {
            if (allowClose || !IsVisible) return;
            UpdateClock(DateTime.Now, true);
            RequestPresent();
            ScheduleNextTick();
            clockTimer.Start();
        }

        public void ResetPosition()
        {
            DockTopRight();
        }

        public void DockBottomLeft()
        {
            settings.DockAnchor = 2;
            ApplyDock(true);
        }

        public void DockTopRight()
        {
            settings.DockAnchor = 1;
            ApplyDock(true);
        }

        public void ApplyPreferredDock()
        {
            if (settings.DockAnchor != 0)
            {
                ApplyDock(true);
            }
        }

        public void BringClockForward()
        {
            if (layeredSurface != null) layeredSurface.BringForward();
        }

        public void ClampToVisibleArea()
        {
            if (!ClockSettings.IsValidCoordinate(surfaceLeft) || !ClockSettings.IsValidCoordinate(surfaceTop))
            {
                ApplyDock(true);
                return;
            }

            Point point = DisplayGeometry.Clamp(
                new Rect(surfaceLeft, surfaceTop, SurfaceWidth, SurfaceHeight), DisplayGeometry.WorkAreas());
            surfaceLeft = point.X;
            surfaceTop = point.Y;
            RememberPosition();
            SyncSurfacePosition();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!allowClose)
            {
                e.Cancel = true;
                hideRequested();
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            clockTimer.Stop();
            clockMenu.Dispose();
            if (hotKeyRegistered && windowHandle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(windowHandle, NativeMethods.ClickThroughHotKeyId);
                hotKeyRegistered = false;
            }

            if (windowSource != null)
            {
                windowSource.RemoveHook(HandleWindowMessage);
                windowSource = null;
            }

            if (layeredSurface != null)
            {
                layeredSurface.Dispose();
                layeredSurface = null;
            }

            base.OnClosed(e);
        }

        private static TextBlock CreateDatePart(string automationName)
        {
            TextBlock text = new TextBlock
            {
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            AutomationProperties.SetName(text, automationName);
            return text;
        }

        private void HandleSourceInitialized(object sender, EventArgs e)
        {
            windowHandle = new WindowInteropHelper(this).Handle;
            windowSource = HwndSource.FromHwnd(windowHandle);
            if (windowSource != null)
            {
                windowSource.AddHook(HandleWindowMessage);
            }

            hotKeyRegistered = NativeMethods.RegisterHotKey(
                windowHandle,
                NativeMethods.ClickThroughHotKeyId,
                NativeMethods.ControlModifier | NativeMethods.AltModifier | NativeMethods.NoRepeatModifier,
                NativeMethods.TKey);
            if (windowSource != null && windowSource.CompositionTarget != null)
            {
                windowSource.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            NativeMethods.DisableTransitions(windowHandle);
            NativeMethods.Cloak(windowHandle, true);
            ParkHostWindow();
            ApplyExtendedStyles();
            ApplyDesktopGlass();
            if (settings.DockAnchor == 0)
            {
                ClampToVisibleArea();
            }
            else
            {
                ApplyDock(true);
            }

            EnsureLayeredSurface();
            RefreshCurrentTime();
        }

        private IntPtr HandleWindowMessage(
            IntPtr handle,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == NativeMethods.DisplayChangeMessage
                || (message == NativeMethods.SettingChangeMessage && wParam.ToInt64() == NativeMethods.SetWorkAreaAction))
            {
                HandleDisplayChanged();
            }

            if (message == NativeMethods.WindowPosChangingMessage)
            {
                NativeMethods.SuppressUnchangedRedraw(handle, lParam);
                return IntPtr.Zero;
            }

            if (message == NativeMethods.MouseActivateMessage)
            {
                handled = true;
                return new IntPtr(NativeMethods.MouseActivateNoActivate);
            }

            if (message == NativeMethods.NcActivateMessage || message == NativeMethods.ActivateMessage)
            {
                handled = true;
                return new IntPtr(1);
            }

            if (message == NativeMethods.EraseBackgroundMessage)
            {
                handled = true;
                return new IntPtr(1);
            }

            if (message == NativeMethods.HotKeyMessage
                && wParam.ToInt64() == NativeMethods.ClickThroughHotKeyId)
            {
                ToggleClickThrough();
                if (!settings.ClickThrough)
                {
                    BringClockForward();
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        private void HandleClockTick(object sender, EventArgs e)
        {
            UpdateClock(DateTime.Now, false);
            if (ClockFaceChanged())
            {
                RequestPresent();
            }

            ScheduleNextTick();
        }

        private void ScheduleNextTick()
        {
            clockTimer.Interval = ClockSchedule.NextTickAfterUpdate(lastClockSample, DateTime.Now, settings.ShowSeconds);
        }

        private void UpdateClock(DateTime now, bool forceDate)
        {
            lastClockSample = now;
            hourRun.Text = ClockFormatter.Hour(now, settings.Use24Hour);
            minuteRun.Text = ClockFormatter.Minute(now);
            secondsRun.Text = ClockFormatter.SecondsSuffix(now, settings.ShowSeconds);
            string period = ClockFormatter.Period(now, settings.Use24Hour);
            periodRun.Text = period.Length == 0 ? string.Empty : " " + period;
            colonRun.Foreground = palette.TimeSecondary;

            int dateStamp = (now.Year * 10000) + (now.Month * 100) + now.Day;
            if (forceDate || dateStamp != lastDateStamp)
            {
                yearText.Text = ClockFormatter.Year(now);
                monthText.Text = ClockFormatter.Month(now);
                dayText.Text = ClockFormatter.Day(now);
                lastDateStamp = dateStamp;
            }
        }

        private void SetClickThrough(bool enabled)
        {
            if (settings.ClickThrough == enabled)
            {
                return;
            }

            settings.ClickThrough = enabled;
            ApplyInteractionState();
            persistSettings();
            RefreshMenuChecks();
            clickThroughChanged(enabled);
        }

        private void RelayoutPreservingCenter()
        {
            double centerX = surfaceLeft + (SurfaceWidth / 2.0);
            double centerY = surfaceTop + (SurfaceHeight / 2.0);
            ApplyLayout(true);
            if (!double.IsNaN(centerX) && !double.IsNaN(centerY))
            {
                surfaceLeft = centerX - (SurfaceWidth / 2.0);
                surfaceTop = centerY - (SurfaceHeight / 2.0);
            }

            if (settings.DockAnchor == 0) ClampToVisibleArea();
            else ApplyDock(false);
            RequestPresent();
        }

        private void ApplyLayout(bool refreshPosition)
        {
            bool showDate = settings.ShowDate;
            double timeWidth = ClockLayout.TimeColumnWidth(settings.ShowSeconds, settings.Use24Hour);
            double designWidth = ClockLayout.DesignWidth(showDate, settings.ShowSeconds, settings.Use24Hour);

            yearColumn.Width = new GridLength(showDate ? ClockLayout.DateColumnWidth : ClockLayout.NoDatePadding);
            leftDivColumn.Width = new GridLength(showDate ? ClockLayout.DividerWidth : 0);
            timeColumn.Width = new GridLength(timeWidth);
            timeScaler.Width = timeWidth;
            rightDivColumn.Width = new GridLength(showDate ? ClockLayout.DividerWidth : 0);
            dateColumn.Width = new GridLength(showDate ? ClockLayout.DateColumnWidth : ClockLayout.NoDatePadding);

            Visibility dateVisibility = showDate ? Visibility.Visible : Visibility.Collapsed;
            yearText.Visibility = dateVisibility;
            monthText.Visibility = dateVisibility;
            dayText.Visibility = dateVisibility;
            leftDivider.Visibility = dateVisibility;
            rightDivider.Visibility = dateVisibility;

            designCanvas.Width = designWidth;
            terminalSurface.Width = designWidth;
            outline.Width = designWidth;
            designCanvas.Clip = ReloadClockChrome.Silhouette(designWidth, ClockLayout.DesignHeight);
            RefreshChrome();

            double scale = ClockLayout.ScaleFactor(settings.ScaleMode);
            Width = designWidth * scale;
            Height = ClockLayout.DesignHeight * scale;

            if (refreshPosition && IsLoaded)
            {
                RememberPosition();
            }
        }

        private void ApplyTheme()
        {
            palette = ClockPalette.Create(settings.ThemeMode, settings.SurfaceTone);
            timeText.Foreground = palette.TimeInk;
            colonRun.Foreground = palette.TimeSecondary;
            secondsRun.Foreground = palette.TimeSecondary;
            periodRun.Foreground = palette.TimeSecondary;
            yearText.Foreground = palette.DateInk;
            monthText.Foreground = palette.DateInk;
            dayText.Foreground = palette.DateInk;
            RefreshChrome();
            ApplyGlyphContrast();
            ApplyOpacity();
            UpdateClock(DateTime.Now, true);
        }

        private void RefreshChrome()
        {
            if (reloadChrome == null || palette == null) return;
            int preset = ClockThemePresets.Match(settings);
            reloadChrome.Configure(palette, settings.ShowDate,
                preset >= 0 ? ClockThemePresets.Captions[preset] : "CUSTOM");
        }

        private void ApplyOpacity()
        {
            if (palette == null)
            {
                return;
            }

            double opacity = OpacityPresets.Normalize(settings.SurfaceOpacity);
            terminalSurface.Opacity = 1.0;
            outline.Opacity = 1.0;
            Background = Brushes.Transparent;
            designCanvas.Background = Brushes.Transparent;
            if (palette.IsOpaque)
            {
                terminalSurface.Background = palette.CreateOpaqueSurface();
            }
            else
            {
                terminalSurface.Background = palette.CreateSurface(opacity);
            }

            outline.BorderBrush = palette.CreateBorder(opacity);
            leftDivider.Background = palette.CreateDivider(opacity);
            rightDivider.Background = palette.CreateDivider(opacity);
            ApplyDesktopGlass();
            RequestPresent();
        }

        private void ApplyDesktopGlass()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            DwmGlass.NeutralizeHover(windowHandle);
        }

        private void EnsureLayeredSurface()
        {
            if (layeredSurface != null)
            {
                ApplyInteractionState();
                return;
            }

            LayeredSurface surface = new LayeredSurface();
            try
            {
                surface.Moved = HandleSurfaceMoved;
                surface.MoveFinished = HandleSurfaceMoveFinished;
                surface.MenuRequested = ShowClockMenu;
                surface.DpiChanged = HandleSurfaceDpiChanged;
                surface.Create(settings.AlwaysOnTop, LayeredSurface.DisplayClassName,
                    (int)Math.Round(surfaceLeft), (int)Math.Round(surfaceTop));
                layeredSurface = surface;
                ApplyInteractionState();
            }
            catch
            {
                surface.Dispose();
                layeredSurface = null;
                throw;
            }
        }

        private void RequestPresent()
        {
            if (allowClose || !IsVisible || presentQueued || layeredSurface == null || layeredSurface.IsDragging)
            {
                return;
            }

            presentQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PresentSurface));
        }

        private void PresentSurface()
        {
            presentQueued = false;
            if (allowClose || layeredSurface == null || !IsVisible || layeredSurface.IsDragging)
            {
                return;
            }

            scaler.UpdateLayout();
            if (layeredSurface.Present(scaler, Width, Height, surfaceLeft, surfaceTop))
            {
                lastPresentedFace = CurrentFace();
                layeredSurface.SetVisible(true);
            }
        }

        private void SyncSurfacePosition()
        {
            if (layeredSurface != null && !layeredSurface.IsDragging)
            {
                layeredSurface.MoveTo(surfaceLeft, surfaceTop);
            }
        }

        private void HandleSurfaceMoved(double left, double top)
        {
            movedDuringDrag |= surfaceLeft != left || surfaceTop != top;
            surfaceLeft = left;
            surfaceTop = top;
        }

        private void HandleSurfaceDpiChanged()
        {
            HandleDisplayChanged();
        }

        private void ParkHostWindow()
        {
            Left = -32000;
            Top = -32000;
        }

        private void HandleHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Math.Abs(Width - lastLayoutWidth) < 0.5 && Math.Abs(Height - lastLayoutHeight) < 0.5)
            {
                return;
            }

            lastLayoutWidth = Width;
            lastLayoutHeight = Height;
            RequestPresent();
        }

        private bool ClockFaceChanged()
        {
            return CurrentFace() != lastPresentedFace;
        }

        private string CurrentFace()
        {
            return hourRun.Text + ":" + minuteRun.Text + secondsRun.Text + periodRun.Text
                + "|" + yearText.Text + monthText.Text + dayText.Text;
        }

        private void HandleSurfaceMoveFinished()
        {
            if (allowClose) return;
            bool moved = movedDuringDrag;
            if (moved)
            {
                settings.DockAnchor = 0;
                movedDuringDrag = false;
            }
            if (displayUpdatePending) ApplyPendingDisplayUpdate();
            else if (moved)
            {
                ClampToVisibleArea();
                persistSettings();
            }
            RefreshCurrentTime();
            RefreshMenuChecks();
        }

        public Forms.ContextMenuStrip SettingsMenu { get { return clockMenu.Menu; } }

        public void RefreshMenuState()
        {
            RefreshMenuChecks();
        }

        private void ShowClockMenu()
        {
            clockMenu.Show(Forms.Cursor.Position);
        }

        private void HandleVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                RefreshCurrentTime();
            }
            else
            {
                clockTimer.Stop();
                if (layeredSurface != null) layeredSurface.SetVisible(false);
            }
        }

        private void ApplyFont()
        {
            int fontMode = settings.FontMode;
            ClockTypography.Apply(timeText, fontMode, ClockTypography.TimeSize, ClockTypography.TimeWeight(fontMode));
            timeText.FontStyle = fontMode == 4 ? FontStyles.Italic : FontStyles.Normal;
            ClockTypography.Apply(yearText, fontMode, ClockTypography.YearSize, ClockTypography.DateWeight(fontMode));
            ClockTypography.Apply(monthText, fontMode, ClockTypography.DateSize, ClockTypography.DateWeight(fontMode));
            ClockTypography.Apply(dayText, fontMode, ClockTypography.DateSize, ClockTypography.DateWeight(fontMode));
            ClockTypography.ApplyRun(hourRun, fontMode);
            ClockTypography.ApplyRun(colonRun, fontMode);
            ClockTypography.ApplyRun(minuteRun, fontMode);
            ClockTypography.ApplyRun(secondsRun, fontMode);
            ClockTypography.ApplyRun(periodRun, fontMode);
            colonRun.FontWeight = FontWeights.Light;
            secondsRun.FontSize = ClockTypography.SecondsSize;
            periodRun.FontSize = ClockTypography.PeriodSize;
            RequestPresent();
        }

        private void ApplyGlyphContrast()
        {
            timeText.Effect = null;
            yearText.Effect = null;
            monthText.Effect = null;
            dayText.Effect = null;
        }

        private void ApplyInteractionState()
        {
            Cursor = Cursors.Arrow;
            Topmost = false;
            ApplyExtendedStyles();
            if (layeredSurface != null)
            {
                layeredSurface.Locked = settings.Locked;
                layeredSurface.SetClickThrough(settings.ClickThrough);
                layeredSurface.SetVisible(IsVisible);
            }

            SetSurfacesTopmost(settings.AlwaysOnTop);
        }

        private void SetSurfacesTopmost(bool topmost)
        {
            if (layeredSurface != null)
            {
                layeredSurface.SetTopmost(topmost);
            }
        }

        private void ApplyExtendedStyles()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            long current = NativeMethods.GetWindowLong(windowHandle, NativeMethods.ExtendedStyleIndex).ToInt64();
            long style = current
                | NativeMethods.ToolWindowStyle
                | NativeMethods.NoActivateStyle;
            style &= ~NativeMethods.LayeredStyle;
            style &= ~NativeMethods.TransparentStyle;

            if (style != current)
            {
                NativeMethods.SetWindowLong(windowHandle, NativeMethods.ExtendedStyleIndex, new IntPtr(style));
                NativeMethods.SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
            }
        }

        private void ApplyDock(bool save)
        {
            Rect workArea = GetCurrentWorkArea();
            double scale = DisplayGeometry.ScaleAt(workArea.Left, workArea.Top);
            double margin = 10.0 * scale;
            if (settings.DockAnchor == 2)
            {
                surfaceLeft = workArea.Left + margin;
                surfaceTop = workArea.Bottom - (Height * scale) - margin;
            }
            else
            {
                surfaceLeft = workArea.Right - (Width * scale) - margin;
                surfaceTop = workArea.Top + margin;
            }

            SyncSurfacePosition();
            RequestPresent();

            RememberPosition();
            if (save)
            {
                persistSettings();
            }
        }

        private Rect GetCurrentWorkArea()
        {
            return DisplayGeometry.WorkAreaAt(surfaceLeft, surfaceTop);
        }

        private void RememberPosition()
        {
            settings.Left = surfaceLeft;
            settings.Top = surfaceTop;
        }

        private void SaveAndRefresh()
        {
            persistSettings();
            RefreshMenuChecks();
        }

        private void RefreshMenuChecks()
        {
            if (clockMenu != null) clockMenu.RefreshState();
        }
    }
}
