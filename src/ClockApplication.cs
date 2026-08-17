using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    internal sealed class ClockApplication : Application
    {
        private readonly ClockSettings settings;
        private readonly EventWaitHandle activationEvent;
        private ClockWindow clockWindow;
        private Forms.NotifyIcon trayIcon;
        private Icon ownedTrayIcon;
        private Forms.ToolStripMenuItem visibilityItem;
        private Forms.ToolStripMenuItem showDateItem;
        private Forms.ToolStripMenuItem showSecondsItem;
        private Forms.ToolStripMenuItem use24HourItem;
        private Forms.ToolStripMenuItem[] inkItems;
        private Forms.ToolStripMenuItem[] surfaceItems;
        private Forms.ToolStripMenuItem[] fontItems;
        private Forms.ToolStripMenuItem[] scaleItems;
        private Forms.ToolStripMenuItem opaqueItem;
        private Forms.ToolStripMenuItem softOpacityItem;
        private Forms.ToolStripMenuItem faintOpacityItem;
        private Forms.ToolStripMenuItem alwaysOnTopItem;
        private Forms.ToolStripMenuItem lockedItem;
        private Forms.ToolStripMenuItem clickThroughItem;
        private Forms.ToolStripMenuItem startupItem;
        private RegisteredWaitHandle activationWait;
        private DispatcherTimer trayTextTimer;
        private bool exiting;
        private bool clickThroughTipShown;

        public ClockApplication(ClockSettings settings, EventWaitHandle activationEvent)
        {
            this.settings = settings;
            this.activationEvent = activationEvent;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            clockWindow = new ClockWindow(
                settings,
                PersistSettings,
                HandleClickThroughChanged,
                StartupManager.IsEnabled,
                SetStartupEnabled,
                HideClock,
                ExitApplication);

            BuildTrayIcon();
            clockWindow.Show();
            if (settings.StartWithWindows)
            {
                SetStartupEnabled(true);
            }

            clockWindow.ApplyPreferredDock();
            PersistSettings();
            if (clockWindow.ClickThrough)
            {
                HandleClickThroughChanged(true);
            }

            if (!clockWindow.IsHotKeyRegistered)
            {
                NotifyHotKeyUnavailable();
            }

            activationWait = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                delegate
                {
                    Dispatcher.BeginInvoke((Action)ShowClock);
                },
                null,
                Timeout.Infinite,
                false);

            SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;

            trayTextTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            trayTextTimer.Tick += delegate { UpdateTrayState(); };
            trayTextTimer.Start();
            UpdateTrayState();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            PersistSettings();
            exiting = true;
            if (clockWindow != null)
            {
                clockWindow.PrepareForExit();
            }

            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;

            if (activationWait != null)
            {
                activationWait.Unregister(null);
                activationWait = null;
            }

            if (trayTextTimer != null)
            {
                trayTextTimer.Stop();
            }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }

            if (ownedTrayIcon != null)
            {
                ownedTrayIcon.Dispose();
                ownedTrayIcon = null;
            }

            base.OnExit(e);
        }

        private void BuildTrayIcon()
        {
            string executablePath = Assembly.GetExecutingAssembly().Location;
            Icon extracted = Icon.ExtractAssociatedIcon(executablePath);
            ownedTrayIcon = extracted == null ? (Icon)SystemIcons.Application.Clone() : (Icon)extracted.Clone();
            if (extracted != null)
            {
                extracted.Dispose();
            }

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            visibilityItem = new Forms.ToolStripMenuItem();
            visibilityItem.Click += delegate
            {
                if (clockWindow.IsVisible)
                {
                    HideClock();
                }
                else
                {
                    ShowClock();
                }
            };

            showDateItem = CheckItem("显示日期", delegate { clockWindow.ToggleShowDate(); });
            showSecondsItem = CheckItem("显示秒钟", delegate { clockWindow.ToggleShowSeconds(); });
            use24HourItem = CheckItem("24 小时制", delegate { clockWindow.ToggleUse24Hour(); });

            Forms.ToolStripMenuItem inkMenu = new Forms.ToolStripMenuItem("数字颜色");
            inkItems = new Forms.ToolStripMenuItem[ClockLooks.InkNames.Length];
            for (int index = 0; index < ClockLooks.InkNames.Length; index++)
            {
                int ink = index;
                inkItems[index] = CheckItem(ClockLooks.InkNames[index], delegate { clockWindow.SetThemeMode(ink); });
                inkMenu.DropDownItems.Add(inkItems[index]);
            }

            Forms.ToolStripMenuItem surfaceMenu = new Forms.ToolStripMenuItem("背景颜色");
            surfaceItems = new Forms.ToolStripMenuItem[ClockLooks.SurfaceNames.Length];
            for (int index = 0; index < ClockLooks.SurfaceNames.Length; index++)
            {
                int tone = index;
                surfaceItems[index] = CheckItem(ClockLooks.SurfaceNames[index], delegate { clockWindow.SetSurfaceTone(tone); });
                surfaceMenu.DropDownItems.Add(surfaceItems[index]);
            }

            Forms.ToolStripMenuItem fontMenu = new Forms.ToolStripMenuItem("字体");
            fontItems = new Forms.ToolStripMenuItem[ClockLooks.FontNames.Length];
            for (int index = 0; index < ClockLooks.FontNames.Length; index++)
            {
                int font = index;
                fontItems[index] = CheckItem(ClockLooks.FontNames[index], delegate { clockWindow.SetFontMode(font); });
                fontMenu.DropDownItems.Add(fontItems[index]);
            }

            Forms.ToolStripMenuItem sizeMenu = new Forms.ToolStripMenuItem("大小");
            scaleItems = new Forms.ToolStripMenuItem[ClockLooks.ScaleNames.Length];
            for (int index = 0; index < ClockLooks.ScaleNames.Length; index++)
            {
                int scale = index;
                scaleItems[index] = CheckItem(ClockLooks.ScaleNames[index], delegate { clockWindow.SetScaleMode(scale); });
                sizeMenu.DropDownItems.Add(scaleItems[index]);
            }

            Forms.ToolStripMenuItem opacityMenu = new Forms.ToolStripMenuItem("背景浓度");
            opaqueItem = CheckItem("较实", delegate { clockWindow.SetSurfaceOpacity(OpacityPresets.Opaque); });
            softOpacityItem = CheckItem("适中", delegate { clockWindow.SetSurfaceOpacity(OpacityPresets.Soft); });
            faintOpacityItem = CheckItem("更透", delegate { clockWindow.SetSurfaceOpacity(OpacityPresets.Faint); });
            opacityMenu.DropDownItems.Add(opaqueItem);
            opacityMenu.DropDownItems.Add(softOpacityItem);
            opacityMenu.DropDownItems.Add(faintOpacityItem);

            alwaysOnTopItem = CheckItem("总在最前", delegate { clockWindow.ToggleAlwaysOnTop(); });
            lockedItem = CheckItem("锁定位置", delegate { clockWindow.ToggleLocked(); });

            clickThroughItem = CheckItem("鼠标穿透（Ctrl+Alt+T）", delegate { clockWindow.ToggleClickThrough(); });
            startupItem = CheckItem("开机自启", delegate { SetStartupEnabled(!settings.StartWithWindows); });

            Forms.ToolStripMenuItem dockBottomLeftItem = new Forms.ToolStripMenuItem("复位到左下角");
            dockBottomLeftItem.Click += delegate
            {
                ShowClock();
                clockWindow.DockBottomLeft();
            };
            Forms.ToolStripMenuItem dockTopRightItem = new Forms.ToolStripMenuItem("复位到右上角");
            dockTopRightItem.Click += delegate
            {
                ShowClock();
                clockWindow.DockTopRight();
            };

            Forms.ToolStripMenuItem exitItem = new Forms.ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };

            menu.Items.Add(visibilityItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(showDateItem);
            menu.Items.Add(showSecondsItem);
            menu.Items.Add(use24HourItem);
            menu.Items.Add(inkMenu);
            menu.Items.Add(surfaceMenu);
            menu.Items.Add(fontMenu);
            menu.Items.Add(sizeMenu);
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(alwaysOnTopItem);
            menu.Items.Add(lockedItem);
            menu.Items.Add(clickThroughItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(dockBottomLeftItem);
            menu.Items.Add(dockTopRightItem);
            menu.Items.Add(exitItem);
            menu.Opening += delegate { UpdateTrayState(); };

            trayIcon = new Forms.NotifyIcon
            {
                Icon = ownedTrayIcon,
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.MouseClick += delegate(object sender, Forms.MouseEventArgs args)
            {
                if (args.Button == Forms.MouseButtons.Left)
                {
                    if (clockWindow.ClickThrough)
                    {
                        clockWindow.DisableClickThrough();
                    }

                    ShowClock();
                }
            };
            trayIcon.MouseDoubleClick += delegate(object sender, Forms.MouseEventArgs args)
            {
                if (args.Button == Forms.MouseButtons.Left)
                {
                    ShowClock();
                }
            };
        }

        private static Forms.ToolStripMenuItem CheckItem(string text, EventHandler handler)
        {
            Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void HandleClickThroughChanged(bool enabled)
        {
            UpdateTrayState();
            if (enabled && trayIcon != null && !clickThroughTipShown)
            {
                clickThroughTipShown = true;
                string body = clockWindow != null && clockWindow.IsHotKeyRegistered
                    ? "按 Ctrl+Alt+T，或单击托盘图标恢复交互。"
                    : "热键不可用，请单击托盘图标或使用托盘菜单恢复交互。";
                trayIcon.ShowBalloonTip(
                    4000,
                    "鼠标穿透已开启",
                    body,
                    Forms.ToolTipIcon.Info);
            }
        }

        private void NotifyHotKeyUnavailable()
        {
            if (trayIcon == null)
            {
                return;
            }

            trayIcon.ShowBalloonTip(
                5000,
                "悬浮时钟",
                "Ctrl+Alt+T 已被其他程序占用，请用托盘菜单切换鼠标穿透。",
                Forms.ToolTipIcon.None);
        }

        private void ShowClock()
        {
            if (exiting || clockWindow == null)
            {
                return;
            }

            if (!clockWindow.IsVisible)
            {
                clockWindow.Show();
            }

            clockWindow.BringClockForward();
            UpdateTrayState();
        }

        private void HideClock()
        {
            if (exiting || clockWindow == null)
            {
                return;
            }

            clockWindow.Hide();
            UpdateTrayState();
        }

        private void SetStartupEnabled(bool enabled)
        {
            settings.StartWithWindows = enabled;
            try
            {
                StartupManager.SetEnabled(enabled);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    clockWindow,
                    "无法更改开机启动项。\n\n" + exception.Message,
                    "悬浮时钟",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            UpdateTrayState();
        }

        private void UpdateTrayState()
        {
            if (trayIcon == null || clockWindow == null)
            {
                return;
            }

            visibilityItem.Text = clockWindow.IsVisible ? "隐藏时钟" : "显示时钟";
            showDateItem.Checked = settings.ShowDate;
            showSecondsItem.Checked = settings.ShowSeconds;
            use24HourItem.Checked = settings.Use24Hour;
            SetExclusiveCheck(inkItems, settings.ThemeMode);
            SetExclusiveCheck(surfaceItems, settings.SurfaceTone);
            SetExclusiveCheck(fontItems, settings.FontMode);
            SetExclusiveCheck(scaleItems, settings.ScaleMode);
            opaqueItem.Checked = OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Opaque);
            softOpacityItem.Checked = OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Soft);
            faintOpacityItem.Checked = OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Faint);
            alwaysOnTopItem.Checked = settings.AlwaysOnTop;
            lockedItem.Checked = settings.Locked;
            clickThroughItem.Checked = clockWindow.ClickThrough;
            clickThroughItem.Text = clockWindow.IsHotKeyRegistered
                ? "鼠标穿透（Ctrl+Alt+T）"
                : "鼠标穿透（热键不可用）";
            startupItem.Checked = settings.StartWithWindows || StartupManager.IsEnabled();
            trayIcon.Text = "悬浮时钟  " + ClockFormatter.TrayTime(DateTime.Now);
        }

        private static void SetExclusiveCheck(Forms.ToolStripMenuItem[] items, int selected)
        {
            if (items == null)
            {
                return;
            }

            for (int index = 0; index < items.Length; index++)
            {
                items[index].Checked = index == selected;
            }
        }

        private void PersistSettings()
        {
            try
            {
                SettingsStore.Save(settings);
            }
            catch
            {
            }
        }

        private void HandleDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (exiting || clockWindow == null)
            {
                return;
            }

            Dispatcher.BeginInvoke((Action)clockWindow.HandleDisplayChanged);
        }

        private void ExitApplication()
        {
            if (exiting)
            {
                return;
            }

            exiting = true;
            if (clockWindow != null)
            {
                clockWindow.PrepareForExit();
            }

            PersistSettings();

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }

            if (clockWindow != null)
            {
                clockWindow.Close();
            }

            Shutdown(0);
        }
    }
}
