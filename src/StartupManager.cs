using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FloatingClock
{
    internal static class StartupManager
    {
        public static string ShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "Floating Clock.lnk");
            }
        }

        public static bool IsEnabled()
        {
            return File.Exists(ShortcutPath);
        }

        public static void ApplyPreference(ClockSettings settings, bool enabled, Action<bool> writeStartup, Action persist, Func<bool> readStartup = null)
        {
            // Treat the shortcut and saved preference as one reversible operation.
            bool previous = settings.StartWithWindows;
            bool previousShortcut = readStartup == null ? previous : readStartup();
            writeStartup(enabled);
            settings.StartWithWindows = enabled;
            try
            {
                persist();
            }
            catch (Exception saveError)
            {
                settings.StartWithWindows = previous;
                try
                {
                    writeStartup(previousShortcut);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("设置未能保存，启动项回滚也失败。请检查开机启动状态。", saveError, rollbackError);
                }
                throw new IOException("设置未能保存，本次开机启动更改已撤销。", saveError);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                }

                return;
            }

            string executablePath = Assembly.GetExecutingAssembly().Location;
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
            CreateShortcut(ShortcutPath, executablePath);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("Windows Script Host is unavailable.");
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember(
                    "TargetPath",
                    BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { targetPath });
                shortcutType.InvokeMember(
                    "WorkingDirectory",
                    BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { Path.GetDirectoryName(targetPath) });
                shortcutType.InvokeMember(
                    "Description",
                    BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { "Floating Clock" });
                shortcutType.InvokeMember(
                    "IconLocation",
                    BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { targetPath + ",0" });
                shortcutType.InvokeMember(
                    "Save",
                    BindingFlags.InvokeMethod,
                    null,
                    shortcut,
                    null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }

                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }
    }
}
