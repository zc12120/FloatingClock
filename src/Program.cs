using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace FloatingClock
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (HasArgument(args, "--self-test"))
            {
                return SelfTest.Run();
            }

            if (HasArgument(args, "--visual-test"))
            {
                return LayeredDragProof.Run();
            }

            if (PreferIntegratedGpu())
            {
                string exe = CurrentExecutable();
                if (!string.IsNullOrEmpty(exe))
                {
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                    return 0;
                }
            }

            string identity = GetIdentityToken();
            string mutexName = @"Local\FloatingClock.Mutex." + identity;
            string activationName = @"Local\FloatingClock.Activate." + identity;
            string acceptedName = activationName + ".Accepted";

            using (Mutex mutex = new Mutex(false, mutexName))
            {
                bool ownsMutex = false;
                try
                {
                    ownsMutex = InstanceCoordinator.AcquireOrActivate(mutex, activationName, acceptedName);
                    if (!ownsMutex) return 0;
                    using (EventWaitHandle acceptedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, acceptedName))
                    using (EventWaitHandle activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationName))
                    {
                        SettingsLoadResult settings = SettingsStore.Load();
                        ClockApplication application = new ClockApplication(settings, activationEvent, acceptedEvent);
                        return application.Run();
                    }
                }
                catch (Exception exception)
                {
                    System.Windows.MessageBox.Show("悬浮时钟无法启动。\n\n" + exception.Message,
                        "悬浮时钟", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return 1;
                }
                finally
                {
                    if (ownsMutex) mutex.ReleaseMutex();
                }
            }
        }

        private static bool PreferIntegratedGpu()
        {
            string exe = CurrentExecutable();
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            const string preferred = "GpuPreference=1;";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string current = key.GetValue(exe) as string;
                    if (string.Equals(current, preferred, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    key.SetValue(exe, preferred);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string CurrentExecutable()
        {
            string location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
            {
                return location;
            }

            string[] args = Environment.GetCommandLineArgs();
            return args.Length > 0 ? args[0] : string.Empty;
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetIdentityToken()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    SecurityIdentifier identifier = identity.User;
                    if (identifier != null) return identifier.Value.Replace('-', '_');
                }
            }
            catch
            {
            }

            return Environment.UserName.Replace(' ', '_');
        }

    }

    internal static class InstanceCoordinator
    {
        internal static bool AcquireOrActivate(Mutex mutex, string activationName, string acceptedName)
        {
            if (Acquire(mutex, 0)) return true;
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 10000)
            {
                EventWaitHandle activation = null;
                EventWaitHandle accepted = null;
                try
                {
                    try { activation = EventWaitHandle.OpenExisting(activationName); }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        if (Acquire(mutex, 100)) return true;
                        continue;
                    }
                    try { accepted = EventWaitHandle.OpenExisting(acceptedName); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    if (accepted != null) accepted.Reset();
                    activation.Set();
                    if (accepted == null)
                    {
                        // Compatibility with pre-1.9.1 instances, which have no acknowledgement event.
                        return Acquire(mutex, 250);
                    }
                    try
                    {
                        int result = WaitHandle.WaitAny(new WaitHandle[] { mutex, accepted }, 250);
                        if (result == 0) return true;
                        if (result == 1) return false;
                    }
                    catch (AbandonedMutexException) { return true; }
                }
                finally
                {
                    if (activation != null) activation.Dispose();
                    if (accepted != null) accepted.Dispose();
                }
                if (Acquire(mutex, 0)) return true;
            }
            throw new TimeoutException("已有时钟实例没有响应。请从托盘退出旧实例后重试。");
        }

        private static bool Acquire(Mutex mutex, int milliseconds)
        {
            try { return mutex.WaitOne(milliseconds); }
            catch (AbandonedMutexException) { return true; }
        }
    }

    internal static class SelfTest
    {
        public static int Run()
        {
            try
            {
                ClockSettings original = ClockSettings.CreateDefault();
                original.ScaleMode = 2;
                original.ThemeMode = 3;
                original.SurfaceTone = 2;
                original.FontMode = 1;
                original.ShowSeconds = true;
                original.ShowDate = false;
                original.Use24Hour = false;
                original.SurfaceOpacity = OpacityPresets.Soft;

                DataContractSerializer serializer = new DataContractSerializer(typeof(ClockSettings));
                ClockSettings restored;
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, original);
                    stream.Position = 0;
                    restored = (ClockSettings)serializer.ReadObject(stream);
                    restored.Normalize();
                }

                if (restored.ScaleMode != 2
                    || restored.ThemeMode != 3
                    || restored.SurfaceTone != 2
                    || restored.FontMode != 1
                    || !restored.ShowSeconds
                    || restored.ShowDate
                    || restored.Use24Hour
                    || !OpacityPresets.Matches(restored.SurfaceOpacity, OpacityPresets.Soft))
                {
                    return 11;
                }

                ClockSettings migrated = ClockSettings.CreateDefault();
                migrated.Version = 4;
                migrated.ThemeMode = 2;
                migrated.ShowSeconds = true;
                migrated.ScaleMode = 2;
                migrated.ShowDate = false;
                migrated.Normalize();
                if (migrated.Version != ClockSettings.CurrentVersion
                    || migrated.ThemeMode != 2
                    || migrated.SurfaceTone != 1
                    || migrated.FontMode != 0
                    || !migrated.ShowSeconds
                    || migrated.ScaleMode != 3
                    || migrated.ShowDate
                    || migrated.DockAnchor != 2
                    || !migrated.StartWithWindows)
                {
                    return 15;
                }

                DateTime sample = new DateTime(2026, 8, 15, 7, 5, 9);
                if (ClockFormatter.Hour(sample, true) != "07"
                    || ClockFormatter.Minute(sample) != "05"
                    || ClockFormatter.SecondsSuffix(sample, false) != string.Empty)
                {
                    return 12;
                }

                if (ClockFormatter.Hour(sample, true) + ":" + ClockFormatter.Minute(sample)
                    + ClockFormatter.SecondsSuffix(sample, true) != "07:05:09")
                {
                    return 13;
                }

                if (ClockFormatter.Year(sample) != "26"
                    || ClockFormatter.Month(sample) != "08"
                    || ClockFormatter.Day(sample) != "15")
                {
                    return 14;
                }

                if (ClockFormatter.Period(sample, true) != string.Empty
                    || ClockFormatter.Period(sample, false) != "AM")
                {
                    return 16;
                }

                if (Math.Abs(ClockLayout.DesignWidth(true, false, true) - 202.0) > 0.001
                    || ClockLooks.ScaleNames.Length != ClockLayout.Scales.Length)
                {
                    return 17;
                }

                if (OpacityPresets.SurfaceAlpha(1.0) >= 255
                    || OpacityPresets.SurfaceAlpha(OpacityPresets.Faint) >= OpacityPresets.SurfaceAlpha(OpacityPresets.Soft))
                {
                    return 21;
                }

                ClockPalette transparentPalette = ClockPalette.Create(0, 0);
                System.Windows.Media.SolidColorBrush transparentSurface =
                    transparentPalette.CreateSurface(OpacityPresets.Faint) as System.Windows.Media.SolidColorBrush;
                System.Windows.Media.SolidColorBrush transparentBorder =
                    transparentPalette.CreateBorder(OpacityPresets.Faint) as System.Windows.Media.SolidColorBrush;
                System.Windows.Media.SolidColorBrush transparentDivider =
                    transparentPalette.CreateDivider(OpacityPresets.Faint) as System.Windows.Media.SolidColorBrush;
                byte transparentAlpha = OpacityPresets.SurfaceAlpha(OpacityPresets.Faint);
                if (transparentSurface == null
                    || transparentBorder == null
                    || transparentDivider == null
                    || transparentAlpha >= 255
                    || transparentSurface.Color.A != transparentAlpha
                    || transparentBorder.Color.A != (byte)Math.Min(255, transparentAlpha + 48)
                    || transparentDivider.Color.A != (byte)Math.Min(255, transparentAlpha + 16))
                {
                    return 22;
                }

                if (ClockLayout.DesignWidth(false, false, true) >= ClockLayout.DesignWidth(true, false, true))
                {
                    return 18;
                }

                if (ClockLayout.TimeColumnWidth(true, true) <= ClockLayout.TimeColumnWidth(false, true))
                {
                    return 19;
                }

                if (ClockLayout.TimeColumnWidth(true, false) <= ClockLayout.TimeColumnWidth(true, true))
                {
                    return 20;
                }

                return RegressionTests.Run();
            }
            catch
            {
                return 99;
            }
        }
    }
}
