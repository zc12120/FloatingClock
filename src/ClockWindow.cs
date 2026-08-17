using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private readonly Viewbox scaler;
        private readonly TextBlock timeText;
        private readonly TextBlock yearText;
        private readonly TextBlock monthText;
        private readonly TextBlock dayText;
        private readonly Run hourRun;
        private readonly Run colonRun;
        private readonly Run minuteRun;
        private readonly Run secondsRun;
        private readonly Run periodRun;
        private readonly ContextMenu clockMenu;
        private readonly DispatcherTimer clockTimer;

        private MenuItem showDateItem;
        private MenuItem showSecondsItem;
        private MenuItem use24HourItem;
        private MenuItem[] inkItems;
        private MenuItem[] surfaceItems;
        private MenuItem[] fontItems;
        private MenuItem[] scaleItems;
        private MenuItem opaqueItem;
        private MenuItem softOpacityItem;
        private MenuItem faintOpacityItem;
        private MenuItem alwaysOnTopItem;
        private MenuItem lockedItem;
        private MenuItem clickThroughItem;
        private MenuItem startupItem;

        private ClockPalette palette;
        private bool allowClose;
        private bool dragging;
        private Point dragOffset;
        private bool hotKeyRegistered;
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
            this.persistSettings = persistSettings;
            this.clickThroughChanged = clickThroughChanged;
            this.getStartupEnabled = getStartupEnabled;
            this.setStartupEnabled = setStartupEnabled;
            this.hideRequested = hideRequested;
            this.exitRequested = exitRequested;

            Title = "FLOAT CLOCK";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            FocusVisualStyle = null;
            Topmost = settings.AlwaysOnTop;
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
                Margin = new Thickness(0, 12, 0, 12),
                IsHitTestVisible = false
            };
            Grid.SetColumn(leftDivider, 1);
            terminalGrid.Children.Add(leftDivider);

            timeText = new TextBlock
            {
                LineHeight = 34,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
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

            Grid.SetColumn(timeText, 2);
            terminalGrid.Children.Add(timeText);

            rightDivider = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 12, 0, 12),
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
                CornerRadius = new CornerRadius(ClockLayout.CornerRadius),
                Child = terminalGrid,
                IsHitTestVisible = false
            };
            AutomationProperties.SetName(terminalSurface, "CLI clock");
            designCanvas.Children.Add(terminalSurface);

            outline = new Border
            {
                Height = ClockLayout.DesignHeight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ClockLayout.CornerRadius),
                IsHitTestVisible = false
            };
            designCanvas.Children.Add(outline);

            scaler = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Child = designCanvas
            };
            Content = scaler;

            clockMenu = BuildContextMenu();
            clockMenu.Opened += delegate
            {
                ApplyMenuPalette();
                RefreshMenuChecks();
            };
            ContextMenu = clockMenu;

            MouseLeftButtonDown += HandleLeftButtonDown;
            MouseMove += HandleMouseMove;
            MouseLeftButtonUp += HandleLeftButtonUp;
            SourceInitialized += HandleSourceInitialized;

            clockTimer = new DispatcherTimer(DispatcherPriority.Background);
            clockTimer.Tick += HandleClockTick;

            ApplyFont();
            ApplyLayout(false);
            ApplyTheme();
            ApplyOpacity();
            ApplyInteractionState();

            if (settings.DockAnchor == 0 && settings.HasPosition)
            {
                Left = settings.Left;
                Top = settings.Top;
            }
            else
            {
                ApplyDock(false);
            }

            UpdateClock(DateTime.Now, true);
            ScheduleNextTick();
            clockTimer.Start();
        }

        public bool ClickThrough
        {
            get { return settings.ClickThrough; }
        }

        public bool IsHotKeyRegistered
        {
            get { return hotKeyRegistered; }
        }

        public void PrepareForExit()
        {
            allowClose = true;
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
            SaveAndRefresh();
        }

        public void SetScaleMode(int scaleMode)
        {
            if (settings.ScaleMode == scaleMode)
            {
                return;
            }

            double centerX = Left + (Width / 2.0);
            double centerY = Top + (Height / 2.0);
            settings.ScaleMode = scaleMode;
            ApplyLayout(true);
            Left = centerX - (Width / 2.0);
            Top = centerY - (Height / 2.0);
            ClampToVisibleArea();
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
            Topmost = settings.AlwaysOnTop;
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
            ApplyExtendedStyles();
            if (settings.DockAnchor == 0)
            {
                ClampToVisibleArea();
            }
            else
            {
                ApplyDock(true);
            }
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
            if (settings.AlwaysOnTop)
            {
                Topmost = true;
                NativeMethods.KeepTopmost(windowHandle);
            }
        }

        public void ClampToVisibleArea()
        {
            Rect virtualArea = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            const double visibleEdge = 20.0;

            if (double.IsNaN(Left) || double.IsNaN(Top))
            {
                ApplyDock(true);
                return;
            }

            Left = Math.Max(
                virtualArea.Left - Width + visibleEdge,
                Math.Min(Left, virtualArea.Right - visibleEdge));
            Top = Math.Max(
                virtualArea.Top - Height + visibleEdge,
                Math.Min(Top, virtualArea.Bottom - visibleEdge));
            RememberPosition();
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

            base.OnClosed(e);
        }

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu
            {
                FontFamily = ClockMenuChrome.Font,
                FontSize = 12,
                Padding = new Thickness(2),
                HasDropShadow = true
            };

            showDateItem = ToggleItem("显示日期", delegate { ToggleShowDate(); });
            showSecondsItem = ToggleItem("显示秒钟", delegate { ToggleShowSeconds(); });
            use24HourItem = ToggleItem("24 小时制", delegate { ToggleUse24Hour(); });

            menu.Items.Add(showDateItem);
            menu.Items.Add(showSecondsItem);
            menu.Items.Add(use24HourItem);
            menu.Items.Add(new Separator());

            MenuItem inkMenu = new MenuItem { Header = "数字颜色" };
            inkItems = new MenuItem[ClockLooks.InkNames.Length];
            for (int index = 0; index < ClockLooks.InkNames.Length; index++)
            {
                int ink = index;
                inkItems[index] = ToggleItem(ClockLooks.InkNames[index], delegate { SetThemeMode(ink); });
                inkMenu.Items.Add(inkItems[index]);
            }

            menu.Items.Add(inkMenu);

            MenuItem surfaceMenu = new MenuItem { Header = "背景颜色" };
            surfaceItems = new MenuItem[ClockLooks.SurfaceNames.Length];
            for (int index = 0; index < ClockLooks.SurfaceNames.Length; index++)
            {
                int tone = index;
                surfaceItems[index] = ToggleItem(ClockLooks.SurfaceNames[index], delegate { SetSurfaceTone(tone); });
                surfaceMenu.Items.Add(surfaceItems[index]);
            }

            menu.Items.Add(surfaceMenu);

            MenuItem fontMenu = new MenuItem { Header = "字体" };
            fontItems = new MenuItem[ClockLooks.FontNames.Length];
            for (int index = 0; index < ClockLooks.FontNames.Length; index++)
            {
                int font = index;
                fontItems[index] = ToggleItem(ClockLooks.FontNames[index], delegate { SetFontMode(font); });
                fontMenu.Items.Add(fontItems[index]);
            }

            menu.Items.Add(fontMenu);

            MenuItem sizeMenu = new MenuItem { Header = "大小" };
            scaleItems = new MenuItem[ClockLooks.ScaleNames.Length];
            for (int index = 0; index < ClockLooks.ScaleNames.Length; index++)
            {
                int scale = index;
                scaleItems[index] = ToggleItem(ClockLooks.ScaleNames[index], delegate { SetScaleMode(scale); });
                sizeMenu.Items.Add(scaleItems[index]);
            }

            menu.Items.Add(sizeMenu);

            MenuItem opacityMenu = new MenuItem { Header = "背景浓度" };
            opaqueItem = ToggleItem("较实", delegate { SetSurfaceOpacity(OpacityPresets.Opaque); });
            softOpacityItem = ToggleItem("适中", delegate { SetSurfaceOpacity(OpacityPresets.Soft); });
            faintOpacityItem = ToggleItem("更透", delegate { SetSurfaceOpacity(OpacityPresets.Faint); });
            opacityMenu.Items.Add(opaqueItem);
            opacityMenu.Items.Add(softOpacityItem);
            opacityMenu.Items.Add(faintOpacityItem);
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Separator());

            alwaysOnTopItem = ToggleItem("总在最前", delegate { ToggleAlwaysOnTop(); });
            lockedItem = ToggleItem("锁定位置", delegate { ToggleLocked(); });
            clickThroughItem = ToggleItem(ClickThroughHeader(), delegate { ToggleClickThrough(); });
            startupItem = ToggleItem("开机自启", delegate
            {
                setStartupEnabled(!settings.StartWithWindows);
                RefreshMenuChecks();
            });

            menu.Items.Add(alwaysOnTopItem);
            menu.Items.Add(lockedItem);
            menu.Items.Add(clickThroughItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new Separator());

            MenuItem dockBottomLeftItem = new MenuItem { Header = "复位到左下角" };
            dockBottomLeftItem.Click += delegate { DockBottomLeft(); };
            MenuItem dockTopRightItem = new MenuItem { Header = "复位到右上角" };
            dockTopRightItem.Click += delegate { DockTopRight(); };
            MenuItem hideItem = new MenuItem { Header = "隐藏到托盘" };
            hideItem.Click += delegate { hideRequested(); };
            MenuItem exitItem = new MenuItem { Header = "退出" };
            exitItem.Click += delegate { exitRequested(); };
            menu.Items.Add(dockBottomLeftItem);
            menu.Items.Add(dockTopRightItem);
            menu.Items.Add(hideItem);
            menu.Items.Add(exitItem);

            return menu;
        }

        private string ClickThroughHeader()
        {
            return hotKeyRegistered || windowHandle == IntPtr.Zero
                ? "鼠标穿透（Ctrl+Alt+T）"
                : "鼠标穿透（热键不可用）";
        }

        private static MenuItem ToggleItem(string header, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsCheckable = false,
                StaysOpenOnClick = false
            };
            item.Click += handler;
            return item;
        }

        private static TextBlock CreateDatePart(string automationName)
        {
            TextBlock text = new TextBlock
            {
                LineHeight = 19,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            AutomationProperties.SetName(text, automationName);
            return text;
        }

        private void HandleLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || settings.Locked)
            {
                return;
            }

            dragging = true;
            dragOffset = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point screen = PointToScreen(e.GetPosition(this));
            Point dip = DeviceToDip(screen.X, screen.Y);
            Left = dip.X - dragOffset.X;
            Top = dip.Y - dragOffset.Y;
        }

        private void HandleLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            RememberPosition();
            persistSettings();
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
            clickThroughItem.Header = ClickThroughHeader();
            if (windowSource != null && windowSource.CompositionTarget != null)
            {
                windowSource.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            NativeMethods.DisableTransitions(windowHandle);
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
        }

        private IntPtr HandleWindowMessage(
            IntPtr handle,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
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
            ScheduleNextTick();
        }

        private void ScheduleNextTick()
        {
            int milliseconds = DateTime.Now.Millisecond;
            clockTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, 1010 - milliseconds));
        }

        private void UpdateClock(DateTime now, bool forceDate)
        {
            hourRun.Text = ClockFormatter.Hour(now, settings.Use24Hour);
            minuteRun.Text = ClockFormatter.Minute(now);
            secondsRun.Text = ClockFormatter.SecondsSuffix(now, settings.ShowSeconds);
            string period = ClockFormatter.Period(now, settings.Use24Hour);
            periodRun.Text = period.Length == 0 ? string.Empty : " " + period;
            colonRun.Foreground = now.Second % 2 == 0 ? palette.TimeInk : palette.TimeSecondary;

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
            double centerX = Left + (Width / 2.0);
            double centerY = Top + (Height / 2.0);
            ApplyLayout(true);
            if (!double.IsNaN(centerX) && !double.IsNaN(centerY))
            {
                Left = centerX - (Width / 2.0);
                Top = centerY - (Height / 2.0);
            }

            ClampToVisibleArea();
        }

        private void ApplyLayout(bool refreshPosition)
        {
            bool showDate = settings.ShowDate;
            double timeWidth = ClockLayout.TimeColumnWidth(settings.ShowSeconds, settings.Use24Hour);
            double designWidth = ClockLayout.DesignWidth(showDate, settings.ShowSeconds, settings.Use24Hour);

            yearColumn.Width = new GridLength(showDate ? ClockLayout.DateColumnWidth : 0);
            leftDivColumn.Width = new GridLength(showDate ? ClockLayout.DividerWidth : 0);
            timeColumn.Width = new GridLength(timeWidth);
            rightDivColumn.Width = new GridLength(showDate ? ClockLayout.DividerWidth : 0);
            dateColumn.Width = new GridLength(showDate ? ClockLayout.DateColumnWidth : 0);

            Visibility dateVisibility = showDate ? Visibility.Visible : Visibility.Collapsed;
            yearText.Visibility = dateVisibility;
            monthText.Visibility = dateVisibility;
            dayText.Visibility = dateVisibility;
            leftDivider.Visibility = dateVisibility;
            rightDivider.Visibility = dateVisibility;

            designCanvas.Width = designWidth;
            terminalSurface.Width = designWidth;
            outline.Width = designWidth;

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
            ApplyGlyphContrast();
            ApplyOpacity();
            ApplyMenuPalette();
            UpdateClock(DateTime.Now, true);
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
            terminalSurface.Background = palette.IsOpaque
                ? palette.CreateOpaqueSurface()
                : palette.CreateSurface(opacity);
            outline.BorderBrush = palette.CreateBorder(opacity);
            leftDivider.Background = palette.CreateDivider(opacity);
            rightDivider.Background = palette.CreateDivider(opacity);
            ApplyDesktopGlass();
        }

        private void ApplyDesktopGlass()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            DwmGlass.Disable(windowHandle);
            if (windowSource != null && windowSource.CompositionTarget != null)
            {
                windowSource.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
        }

        private void ApplyFont()
        {
            int fontMode = settings.FontMode;
            ClockTypography.Apply(timeText, fontMode, ClockTypography.TimeSize, ClockTypography.TimeWeight(fontMode));
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
        }

        private void ApplyGlyphContrast()
        {
            timeText.Effect = null;
            yearText.Effect = null;
            monthText.Effect = null;
            dayText.Effect = null;
        }

        private void ApplyMenuPalette()
        {
            if (clockMenu == null)
            {
                return;
            }

            clockMenu.FontFamily = ClockMenuChrome.Font;
            clockMenu.Background = ClockMenuChrome.Surface;
            clockMenu.Foreground = ClockMenuChrome.Foreground;
            clockMenu.BorderBrush = ClockMenuChrome.Border;
            clockMenu.BorderThickness = new Thickness(1);
            OverrideMenuColors(clockMenu.Resources);

            Style itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(MenuItem.FontFamilyProperty, ClockMenuChrome.Font));
            itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, ClockMenuChrome.Foreground));
            itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, ClockMenuChrome.Surface));
            itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 5, 18, 5)));
            itemStyle.Setters.Add(new Setter(MenuItem.BorderThicknessProperty, new Thickness(0)));
            clockMenu.Resources[typeof(MenuItem)] = itemStyle;

            Style separatorStyle = new Style(typeof(Separator));
            separatorStyle.Setters.Add(new Setter(Separator.BackgroundProperty, ClockMenuChrome.Separator));
            separatorStyle.Setters.Add(new Setter(Separator.MarginProperty, new Thickness(7, 3, 7, 3)));
            separatorStyle.Setters.Add(new Setter(Separator.HeightProperty, 1.0));
            clockMenu.Resources[typeof(Separator)] = separatorStyle;

            ApplyItemChrome(clockMenu.Items);
        }

        private static void OverrideMenuColors(ResourceDictionary resources)
        {
            resources[SystemColors.MenuBrushKey] = ClockMenuChrome.Surface;
            resources[SystemColors.MenuBarBrushKey] = ClockMenuChrome.Surface;
            resources[SystemColors.ControlBrushKey] = ClockMenuChrome.Surface;
            resources[SystemColors.WindowBrushKey] = ClockMenuChrome.Surface;
            resources[SystemColors.HighlightBrushKey] = ClockMenuChrome.Highlight;
            resources[SystemColors.HighlightTextBrushKey] = ClockMenuChrome.Foreground;
            resources[SystemColors.InactiveSelectionHighlightBrushKey] = ClockMenuChrome.Highlight;
            resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = ClockMenuChrome.Foreground;
            resources[SystemColors.MenuTextBrushKey] = ClockMenuChrome.Foreground;
            resources[SystemColors.ControlTextBrushKey] = ClockMenuChrome.Foreground;
        }

        private static void ApplyItemChrome(ItemCollection items)
        {
            foreach (object entry in items)
            {
                MenuItem item = entry as MenuItem;
                if (item == null)
                {
                    continue;
                }

                item.FontFamily = ClockMenuChrome.Font;
                item.Foreground = ClockMenuChrome.Foreground;
                item.Background = ClockMenuChrome.Surface;
                item.IsCheckable = false;
                OverrideMenuColors(item.Resources);
                if (item.HasItems)
                {
                    ApplyItemChrome(item.Items);
                }
            }
        }

        private void ApplyInteractionState()
        {
            Cursor = settings.Locked ? Cursors.Arrow : Cursors.SizeAll;
            Topmost = settings.AlwaysOnTop;
            ApplyExtendedStyles();
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
            if (settings.ClickThrough)
            {
                style |= NativeMethods.TransparentStyle;
            }
            else
            {
                style &= ~NativeMethods.TransparentStyle;
            }

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
            const double margin = 10.0;
            if (settings.DockAnchor == 2)
            {
                Left = workArea.Left + margin;
                Top = workArea.Bottom - Height - margin;
            }
            else
            {
                Left = workArea.Right - Width - margin;
                Top = workArea.Top + margin;
            }

            RememberPosition();
            if (save)
            {
                persistSettings();
            }
        }

        private Rect GetCurrentWorkArea()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return SystemParameters.WorkArea;
            }

            try
            {
                Forms.Screen screen = Forms.Screen.FromHandle(windowHandle);
                System.Drawing.Rectangle area = screen.WorkingArea;
                Point topLeft = DeviceToDip(area.Left, area.Top);
                Point bottomRight = DeviceToDip(area.Right, area.Bottom);
                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return SystemParameters.WorkArea;
            }
        }

        private Point DeviceToDip(double x, double y)
        {
            PresentationSource source = windowSource ?? PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
            }

            return new Point(x, y);
        }

        private void RememberPosition()
        {
            settings.Left = Left;
            settings.Top = Top;
        }

        private void SaveAndRefresh()
        {
            persistSettings();
            RefreshMenuChecks();
        }

        private void RefreshMenuChecks()
        {
            MarkItem(showDateItem, "显示日期", settings.ShowDate);
            MarkItem(showSecondsItem, "显示秒钟", settings.ShowSeconds);
            MarkItem(use24HourItem, "24 小时制", settings.Use24Hour);
            MarkExclusive(inkItems, ClockLooks.InkNames, settings.ThemeMode);
            MarkExclusive(surfaceItems, ClockLooks.SurfaceNames, settings.SurfaceTone);
            MarkExclusive(fontItems, ClockLooks.FontNames, settings.FontMode);
            MarkExclusive(scaleItems, ClockLooks.ScaleNames, settings.ScaleMode);
            MarkItem(opaqueItem, "较实", OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Opaque));
            MarkItem(softOpacityItem, "适中", OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Soft));
            MarkItem(faintOpacityItem, "更透", OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Faint));
            MarkItem(alwaysOnTopItem, "总在最前", settings.AlwaysOnTop);
            MarkItem(lockedItem, "锁定位置", settings.Locked);
            MarkItem(clickThroughItem, ClickThroughHeader(), settings.ClickThrough);
            MarkItem(startupItem, "开机自启", settings.StartWithWindows || getStartupEnabled());
        }

        private static void MarkExclusive(MenuItem[] items, string[] names, int selected)
        {
            if (items == null)
            {
                return;
            }

            for (int index = 0; index < items.Length; index++)
            {
                MarkItem(items[index], names[index], index == selected);
            }
        }

        private static void MarkItem(MenuItem item, string label, bool on)
        {
            if (item == null)
            {
                return;
            }

            item.IsCheckable = false;
            item.IsChecked = false;
            item.Header = (on ? "✓  " : "    ") + label;
        }
    }
}
