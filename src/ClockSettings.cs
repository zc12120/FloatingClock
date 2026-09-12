using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace FloatingClock
{
    [DataContract(Name = "FloatingClockSettings")]
    internal sealed class ClockSettings
    {
        public const int CurrentVersion = 11;

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

        [DataMember(Order = 17)]
        public bool PositionInPixels { get; set; }

        [OnDeserializing]
        private void InitializeMissingMembers(StreamingContext context)
        {
            InitializeDefaults();
            // Missing version/coordinate markers identify legacy files, not current defaults.
            Version = 0;
            PositionInPixels = false;
        }

        private void InitializeDefaults()
        {
            Version = CurrentVersion;
            Left = double.NaN;
            Top = double.NaN;
            ShowDate = true;
            ShowSeconds = false;
            Use24Hour = true;
            AlwaysOnTop = true;
            Locked = false;
            ClickThrough = false;
            ThemeMode = 2;
            ScaleMode = 2;
            SurfaceOpacity = OpacityPresets.Soft;
            SurfaceTone = 2;
            FontMode = 4;
            DockAnchor = 2;
            StartWithWindows = true;
            PositionInPixels = true;
        }

        internal static bool IsValidCoordinate(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value)
                && value >= int.MinValue && value <= int.MaxValue;
        }

        public bool HasPosition
        {
            get
            {
                return IsValidCoordinate(Left) && IsValidCoordinate(Top);
            }
        }

        public static ClockSettings CreateDefault()
        {
            ClockSettings settings = new ClockSettings();
            settings.InitializeDefaults();
            return settings;
        }

        public void Normalize()
        {
            if (Version > CurrentVersion)
                throw new SettingsVersionException(Version);
            if (Version < 0)
                throw new SerializationException("Invalid settings version.");
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
                AlwaysOnTop = true;
            }

            if (DockAnchor < 0 || DockAnchor > 2)
            {
                DockAnchor = 2;
            }

            SurfaceOpacity = OpacityPresets.Normalize(SurfaceOpacity);

            if (!HasPosition)
            {
                Left = double.NaN;
                Top = double.NaN;
            }

            Version = CurrentVersion;
        }

        public void MigratePosition(double legacyScale)
        {
            if (PositionInPixels) return;
            if (HasPosition && legacyScale > 0 && !double.IsInfinity(legacyScale))
            {
                Left *= legacyScale;
                Top *= legacyScale;
            }
            else
            {
                Left = double.NaN;
                Top = double.NaN;
            }
            if (!HasPosition)
            {
                Left = double.NaN;
                Top = double.NaN;
            }
            PositionInPixels = true;
        }
    }

    internal sealed class SettingsVersionException : SerializationException
    {
        public SettingsVersionException(int version)
            : base("Settings version " + version + " is newer than supported version " + ClockSettings.CurrentVersion + ".") { }
    }

    internal sealed class SettingsLoadResult
    {
        public ClockSettings Settings { get; private set; }
        public bool CanSave { get; private set; }
        public string Notice { get; private set; }

        public SettingsLoadResult(ClockSettings settings, bool canSave, string notice)
        {
            Settings = settings;
            CanSave = canSave;
            Notice = notice;
        }
    }

    internal static class SettingsStore
    {
        internal const string ContractNamespace = "http://schemas.datacontract.org/2004/07/FloatingClock";
        internal const int MaximumCharacters = 65536;
        private static readonly string[] MemberNames = {
            "Version", "Left", "Top", "ShowDate", "ShowSeconds", "Use24Hour", "AlwaysOnTop",
            "Locked", "ClickThrough", "ThemeMode", "ScaleMode", "SurfaceOpacity", "SurfaceTone",
            "FontMode", "DockAnchor", "StartWithWindows", "PositionInPixels"
        };

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

        public static SettingsLoadResult Load()
        {
            return LoadFrom(SettingsPath);
        }

        internal static SettingsLoadResult LoadFrom(string path)
        {
            try
            {
                return new SettingsLoadResult(Deserialize(ReadBytes(path)), true, null);
            }
            catch (FileNotFoundException)
            {
                return new SettingsLoadResult(ClockSettings.CreateDefault(), true, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new SettingsLoadResult(ClockSettings.CreateDefault(), true, null);
            }
            catch (SettingsVersionException exception)
            {
                return new SettingsLoadResult(ClockSettings.CreateDefault(), false,
                    "设置来自更新版本。原文件保持不变，本次以临时默认设置运行，不保存设置或更改开机自启。\n" + exception.Message);
            }
            catch (Exception exception)
            {
                if (!IsReadFailure(exception)) throw;
                ClockSettings fallback = ClockSettings.CreateDefault();
                bool recovered = false;
                try
                {
                    fallback = Deserialize(ReadBytes(path + ".bak"));
                    recovered = true;
                }
                catch (Exception backupError)
                {
                    if (!IsReadFailure(backupError)) throw;
                }
                return new SettingsLoadResult(fallback, false,
                    (recovered ? "设置读取失败，已临时使用上次有效备份。" : "设置读取失败，已临时使用默认设置。")
                    + "原文件保持不变，本次不保存设置或更改开机自启。请修复设置文件后重启。\n" + exception.Message);
            }
        }

        private static bool IsReadFailure(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException
                || exception is SerializationException || exception is XmlException
                || exception is System.Security.SecurityException || exception is FormatException
                || exception is OverflowException;
        }

        private static byte[] ReadBytes(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumCharacters * 4L)
                    throw new SerializationException("Settings file exceeds the size limit.");
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }

        internal static ClockSettings Deserialize(byte[] bytes)
        {
            XmlReaderSettings readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacters
            };
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (MemoryStream stream = new MemoryStream(bytes))
            using (XmlReader reader = XmlReader.Create(stream, readerSettings)) document.Load(reader);
            XmlElement root = document.DocumentElement;
            if (root == null || root.LocalName != "FloatingClockSettings" || root.NamespaceURI != ContractNamespace)
                throw new SerializationException("Invalid settings root or namespace.");
            int lastMember = -1;
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                int index = Array.IndexOf(MemberNames, node.LocalName);
                if (index < 0) continue;
                if (node.NamespaceURI != ContractNamespace || index <= lastMember)
                    throw new SerializationException("Duplicate, out-of-order or invalid settings member: " + node.LocalName);
                lastMember = index;
            }
            ClockSettings settings;
            using (XmlNodeReader reader = new XmlNodeReader(document))
                settings = (ClockSettings)new DataContractSerializer(typeof(ClockSettings)).ReadObject(reader);
            if (settings == null) throw new SerializationException("Empty settings document.");
            settings.Normalize();
            return settings;
        }

        private static byte[] Serialize(ClockSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.Version > ClockSettings.CurrentVersion) throw new SettingsVersionException(settings.Version);
            XmlWriterSettings writerSettings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new System.Text.UTF8Encoding(false)
            };
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, writerSettings))
                    new DataContractSerializer(typeof(ClockSettings)).WriteObject(writer, settings);
                return stream.ToArray();
            }
        }

        private static bool SameBytes(byte[] first, byte[] second)
        {
            if (first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index]) return false;
            return true;
        }

        public static void Save(ClockSettings settings)
        {
            SaveTo(SettingsPath, settings);
        }

        internal static void SaveTo(string path, ClockSettings settings)
        {
            path = Path.GetFullPath(path);
            byte[] data = Serialize(settings);
            // Validate before writing, including files changed by a newer installation.
            byte[] previous = null;
            try { previous = ReadBytes(path); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (previous != null)
            {
                Deserialize(previous);
                if (SameBytes(previous, data)) return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                if (previous != null)
                {
                    if (!SameBytes(previous, ReadBytes(path)))
                        throw new IOException("Settings changed in another process; refusing to overwrite them.");
                    // Keep the last valid version for recovery instead of deleting it.
                    File.Replace(temporaryPath, path, path + ".bak");
                }
                else File.Move(temporaryPath, path);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
