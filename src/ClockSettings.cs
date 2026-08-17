using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace FloatingClock
{
    [DataContract(Name = "FloatingClockSettings")]
    internal sealed class ClockSettings
    {
        public const int CurrentVersion = 10;

        [DataMember(Order = 1)]
        public int Version { get; set; }

        [DataMember(Order = 2)]
        public double Left { get; set; }

        [DataMember(Order = 3)]
        public double Top { get; set; }

        [DataMember(Order = 4)]
        public bool ShowDate { get; set; }

        [DataMember(Order = 5)]
        public bool ShowSeconds { get; set; }

        [DataMember(Order = 6)]
        public bool Use24Hour { get; set; }

        [DataMember(Order = 7)]
        public bool AlwaysOnTop { get; set; }

        [DataMember(Order = 8)]
        public bool Locked { get; set; }

        [DataMember(Order = 9)]
        public bool ClickThrough { get; set; }

        [DataMember(Order = 10)]
        public int ThemeMode { get; set; }

        [DataMember(Order = 11)]
        public int ScaleMode { get; set; }

        [DataMember(Order = 12)]
        public double SurfaceOpacity { get; set; }

        [DataMember(Order = 13)]
        public int SurfaceTone { get; set; }

        [DataMember(Order = 14)]
        public int FontMode { get; set; }

        [DataMember(Order = 15)]
        public int DockAnchor { get; set; }

        [DataMember(Order = 16)]
        public bool StartWithWindows { get; set; }

        public bool HasPosition
        {
            get
            {
                return !double.IsNaN(Left) && !double.IsInfinity(Left)
                    && !double.IsNaN(Top) && !double.IsInfinity(Top);
            }
        }

        public static ClockSettings CreateDefault()
        {
            return new ClockSettings
            {
                Version = CurrentVersion,
                Left = double.NaN,
                Top = double.NaN,
                ShowDate = true,
                ShowSeconds = false,
                Use24Hour = true,
                AlwaysOnTop = true,
                Locked = false,
                ClickThrough = false,
                ThemeMode = 0,
                ScaleMode = 2,
                SurfaceOpacity = OpacityPresets.Soft,
                SurfaceTone = 0,
                FontMode = 0,
                DockAnchor = 2,
                StartWithWindows = true
            };
        }

        public void Normalize()
        {
            if (Version < 7 && OpacityPresets.Matches(SurfaceOpacity, OpacityPresets.Opaque))
            {
                SurfaceOpacity = OpacityPresets.Soft;
            }

            if (Version < 8)
            {
                SurfaceTone = ThemeMode == 2 ? 1 : 0;
            }

            if (Version < 9)
            {
                if (ScaleMode <= 0)
                {
                    ScaleMode = 1;
                }
                else if (ScaleMode == 1)
                {
                    ScaleMode = 2;
                }
                else
                {
                    ScaleMode = 3;
                }

                FontMode = 0;
            }

            if (ThemeMode < 0 || ThemeMode >= ClockLooks.InkNames.Length)
            {
                ThemeMode = 0;
            }

            if (SurfaceTone < 0 || SurfaceTone >= ClockLooks.SurfaceNames.Length)
            {
                SurfaceTone = 0;
            }

            if (FontMode < 0 || FontMode >= ClockLooks.FontNames.Length)
            {
                FontMode = 0;
            }

            if (ScaleMode < 0 || ScaleMode >= ClockLooks.ScaleNames.Length)
            {
                ScaleMode = 2;
            }

            if (Version < 10)
            {
                DockAnchor = 2;
                StartWithWindows = true;
                AlwaysOnTop = true;
            }

            if (DockAnchor < 0 || DockAnchor > 2)
            {
                DockAnchor = 2;
            }

            SurfaceOpacity = OpacityPresets.Normalize(SurfaceOpacity);

            if (double.IsInfinity(Left) || double.IsInfinity(Top))
            {
                Left = double.NaN;
                Top = double.NaN;
            }

            Version = CurrentVersion;
        }
    }

    internal static class SettingsStore
    {
        private static readonly DataContractSerializer Serializer =
            new DataContractSerializer(typeof(ClockSettings));

        public static string FolderPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FloatingClock");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(FolderPath, "settings.xml"); }
        }

        public static ClockSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                return ClockSettings.CreateDefault();
            }

            try
            {
                using (FileStream stream = File.OpenRead(SettingsPath))
                {
                    ClockSettings settings = (ClockSettings)Serializer.ReadObject(stream);
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return ClockSettings.CreateDefault();
            }
        }

        public static void Save(ClockSettings settings)
        {
            Directory.CreateDirectory(FolderPath);
            string temporaryPath = SettingsPath + ".tmp";
            string backupPath = SettingsPath + ".bak";
            XmlWriterSettings writerSettings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new System.Text.UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(temporaryPath, writerSettings))
            {
                Serializer.WriteObject(writer, settings);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, backupPath);
                try
                {
                    File.Delete(backupPath);
                }
                catch
                {
                }
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
    }
}
