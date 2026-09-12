using System;
using System.IO;
using System.Text;

namespace FloatingClock
{
    // All fixtures live in a unique temporary folder, never in the user's settings directory.
    internal static class SettingsRegressionTests
    {
        private const string Prefix = "<FloatingClockSettings xmlns=\"http://schemas.datacontract.org/2004/07/FloatingClock\">";
        private const string Suffix = "</FloatingClockSettings>";

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void DefaultsAndValidation()
        {
            ClockSettings partial = SettingsStore.Deserialize(Encoding.UTF8.GetBytes(Prefix
                + "<Version>11</Version><StartWithWindows>false</StartWithWindows>" + Suffix));
            Assert(!partial.StartWithWindows && partial.ShowDate && partial.Use24Hour && partial.AlwaysOnTop,
                "Missing members must use defaults without overwriting an explicit opt-out");
            Assert(!partial.HasPosition && partial.ScaleMode == 2 && !partial.PositionInPixels,
                "Missing coordinates or legacy coordinate marker were lost");
            ClockSettings explicitValues = SettingsStore.Deserialize(Encoding.UTF8.GetBytes(Prefix
                + "<Version>11</Version><ShowDate>false</ShowDate><Use24Hour>false</Use24Hour><AlwaysOnTop>false</AlwaysOnTop><ScaleMode>0</ScaleMode>" + Suffix));
            Assert(!explicitValues.ShowDate && !explicitValues.Use24Hour && !explicitValues.AlwaysOnTop && explicitValues.ScaleMode == 0,
                "Explicit false and zero values must survive deserialization");

            string[] invalid = {
                "<FloatingClockSettings><Version>11</Version></FloatingClockSettings>",
                Prefix + "<Version>12</Version><FutureChoice>keep</FutureChoice>" + Suffix,
                Prefix + "<Version>-1</Version>" + Suffix,
                Prefix + "<Version>11</Version><ScaleMode>bad</ScaleMode>" + Suffix,
                Prefix + "<ShowDate>true</ShowDate><ShowDate>false</ShowDate>" + Suffix,
                Prefix + "<PositionInPixels>true</PositionInPixels><StartWithWindows>false</StartWithWindows>" + Suffix,
                "<!DOCTYPE FloatingClockSettings [<!ENTITY date \"true\">]>" + Prefix + "<ShowDate>&date;</ShowDate>" + Suffix,
                Prefix + "<!--" + new string('x', SettingsStore.MaximumCharacters) + "-->" + Suffix
            };
            foreach (string xml in invalid)
            {
                bool rejected = false;
                try { SettingsStore.Deserialize(Encoding.UTF8.GetBytes(xml)); }
                catch (Exception) { rejected = true; }
                Assert(rejected, "Invalid or incompatible settings were accepted");
            }
        }

        public static void AtomicStorageAndRecovery()
        {
            string root = Path.Combine(Path.GetTempPath(), "FloatingClock-storage-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "settings.xml");
            try
            {
                SettingsLoadResult missing = SettingsStore.LoadFrom(path);
                Assert(missing.CanSave && !File.Exists(path), "Loading a missing file should not write defaults");
                ClockSettings settings = missing.Settings;
                settings.StartWithWindows = false;
                SettingsStore.SaveTo(path, settings);
                string initial = File.ReadAllText(path);
                DateTime fixedTime = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, fixedTime);
                SettingsStore.SaveTo(path, settings);
                Assert(File.GetLastWriteTimeUtc(path) == fixedTime && !File.Exists(path + ".bak"),
                    "Saving unchanged settings must not rewrite the file or rotate the backup");
                settings.ThemeMode = 4;
                SettingsStore.SaveTo(path, settings);
                Assert(File.ReadAllText(path + ".bak") == initial, "Atomic replacement did not retain the previous version");
                SettingsLoadResult restored = SettingsStore.LoadFrom(path);
                Assert(restored.CanSave && restored.Settings.ThemeMode == 4 && !restored.Settings.StartWithWindows,
                    "Settings round trip lost values");
                string validCurrent = File.ReadAllText(path);

                File.WriteAllText(path, "<invalid");
                SettingsLoadResult recovered = SettingsStore.LoadFrom(path);
                Assert(!recovered.CanSave && !recovered.Settings.StartWithWindows && !string.IsNullOrEmpty(recovered.Notice),
                    "A damaged file should use a valid backup only in read-only recovery mode");
                Assert(File.ReadAllText(path) == "<invalid", "Recovery overwrote the damaged original");
                bool refused = false;
                try { SettingsStore.SaveTo(path, settings); }
                catch (Exception) { refused = true; }
                Assert(refused && File.ReadAllText(path) == "<invalid", "Saving must not destroy an externally damaged file");

                string future = validCurrent.Replace("<Version>11</Version>", "<Version>12</Version>");
                File.WriteAllText(path, future);
                SettingsLoadResult newer = SettingsStore.LoadFrom(path);
                Assert(!newer.CanSave && File.ReadAllText(path) == future, "Newer settings were downgraded");
                refused = false;
                try { SettingsStore.SaveTo(path, settings); }
                catch (SettingsVersionException) { refused = true; }
                Assert(refused && File.ReadAllText(path) == future, "Saving overwrote a newer configuration");

                File.WriteAllText(path, validCurrent);
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    SettingsLoadResult unavailable = SettingsStore.LoadFrom(path);
                    Assert(!unavailable.CanSave, "A temporarily locked file was mistaken for a fresh installation");
                }
                Assert(File.ReadAllText(path) == validCurrent, "A read failure changed the original configuration");
                Assert(Directory.GetFiles(root, "*.tmp").Length == 0, "An atomic save left a temporary file behind");
            }
            finally
            {
                // Only this test's GUID-named fixture directory is removed.
                Directory.Delete(root, true);
            }
        }

        public static void CoordinatesAndStartupRollback()
        {
            double[] invalid = { double.MaxValue, double.MinValue, 1E200, -1E200, double.NaN, double.PositiveInfinity };
            foreach (double coordinate in invalid)
            {
                ClockSettings settings = ClockSettings.CreateDefault();
                settings.Left = coordinate;
                settings.Top = 100;
                settings.Normalize();
                Assert(!settings.HasPosition && double.IsNaN(settings.Left) && double.IsNaN(settings.Top),
                    "Invalid coordinates must be cleared as a pair");
            }
            ClockSettings migration = ClockSettings.CreateDefault();
            migration.Left = int.MaxValue - 1;
            migration.Top = 100;
            migration.PositionInPixels = false;
            migration.MigratePosition(2);
            Assert(!migration.HasPosition && migration.PositionInPixels, "DPI migration overflow was not rejected");
            migration = ClockSettings.CreateDefault();
            migration.Left = -1000;
            migration.Top = -200;
            migration.PositionInPixels = false;
            migration.MigratePosition(1.5);
            Assert(migration.Left == -1500 && migration.Top == -300, "Valid negative monitor coordinates were lost");

            ClockSettings preference = ClockSettings.CreateDefault();
            bool shortcut = true;
            int writes = 0;
            bool failed = false;
            try
            {
                StartupManager.ApplyPreference(preference, false, delegate(bool enabled) { shortcut = enabled; writes++; },
                    delegate { throw new IOException("Simulated disk failure"); });
            }
            catch (IOException) { failed = true; }
            Assert(failed && shortcut && preference.StartWithWindows && writes == 2,
                "Persistence failure did not roll back both shortcut and in-memory preference");
            writes = 0;
            bool combined = false;
            try
            {
                StartupManager.ApplyPreference(preference, false, delegate(bool enabled)
                {
                    if (++writes == 2) throw new IOException("Simulated rollback failure");
                }, delegate { throw new IOException("Simulated persistence failure"); });
            }
            catch (AggregateException exception) { combined = exception.InnerExceptions.Count == 2; }
            Assert(combined && preference.StartWithWindows, "Rollback failure must report both errors, never success");
            shortcut = false;
            failed = false;
            try
            {
                StartupManager.ApplyPreference(preference, true, delegate(bool enabled) { shortcut = enabled; },
                    delegate { throw new IOException("Simulated startup reconciliation failure"); }, delegate { return shortcut; });
            }
            catch (IOException) { failed = true; }
            Assert(failed && !shortcut && preference.StartWithWindows,
                "Rollback restored a stale saved preference instead of the actual previous shortcut state");
        }
    }
}
