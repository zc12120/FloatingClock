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
            SetStartupEnabled(settings.StartWithWindows);

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
            SystemEvents.TimeChanged += HandleTimeChanged;
            SystemEvents.PowerModeChanged += HandlePowerModeChanged;

            trayTextTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = ClockSchedule.NextTick(DateTime.Now, false)
            };
            trayTextTimer.Tick += delegate
            {
                UpdateTrayState();
                trayTextTimer.Interval = ClockSchedule.NextTick(DateTime.Now, false);
            };
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
            SystemEvents.TimeChanged -= HandleTimeChanged;
            SystemEvents.PowerModeChanged -= HandlePowerModeChanged;

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

            trayIcon = new Forms.NotifyIcon
            {
                Icon = ownedTrayIcon,
                ContextMenuStrip = clockWindow.SettingsMenu,
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
            try
            {
                StartupManager.ApplyPreference(settings, enabled, StartupManager.SetEnabled, PersistSettings);
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

            clockWindow.RefreshMenuState();
            trayIcon.Text = "悬浮时钟  " + ClockFormatter.TrayTime(DateTime.Now);
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

        private void HandlePowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) HandleTimeChanged(sender, EventArgs.Empty);
        }

        private void HandleTimeChanged(object sender, EventArgs e)
        {
            if (exiting || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (exiting || clockWindow == null) return;
                clockWindow.RefreshCurrentTime();
                UpdateTrayState();
                if (trayTextTimer != null) trayTextTimer.Interval = ClockSchedule.NextTick(DateTime.Now, false);
            }));
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
