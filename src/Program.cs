using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Principal;
using System.Threading;

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

            string identity = GetIdentityToken();
            string mutexName = @"Local\FloatingClock.Mutex." + identity;
            string activationName = @"Local\FloatingClock.Activate." + identity;
            bool ownsMutex;

            using (Mutex mutex = new Mutex(true, mutexName, out ownsMutex))
            {
                if (!ownsMutex)
                {
                    SignalExistingInstance(activationName);
                    return 0;
                }

                bool created;
                using (EventWaitHandle activationEvent = new EventWaitHandle(
                    false,
                    EventResetMode.AutoReset,
                    activationName,
                    out created))
                {
                    ClockSettings settings = SettingsStore.Load();
                    ClockApplication application = new ClockApplication(settings, activationEvent);
                    return application.Run();
                }
            }
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
                SecurityIdentifier identifier = WindowsIdentity.GetCurrent().User;
                if (identifier != null)
                {
                    return identifier.Value.Replace('-', '_');
                }
            }
            catch
            {
            }

            return Environment.UserName.Replace(' ', '_');
        }

        private static void SignalExistingInstance(string activationName)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using (EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(activationName))
                    {
                        activationEvent.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(100);
                }
                catch
                {
                    return;
                }
            }
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

                if (Math.Abs(ClockLayout.DesignWidth(true, false, true) - 198.0) > 0.001
                    || ClockLooks.ScaleNames.Length != ClockLayout.Scales.Length)
                {
                    return 17;
                }

                if (OpacityPresets.SurfaceAlpha(1.0) >= 255
                    || OpacityPresets.SurfaceAlpha(OpacityPresets.Faint) >= OpacityPresets.SurfaceAlpha(OpacityPresets.Soft))
                {
                    return 21;
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

                return 0;
            }
            catch
            {
                return 99;
            }
        }
    }
}
