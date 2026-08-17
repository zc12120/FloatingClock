using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FloatingClock
{
    internal static class ClockLooks
    {
        public static readonly string[] InkNames =
        {
            "荧光绿",
            "柔和绿",
            "冷白",
            "电光青",
            "琥珀",
            "玫粉",
            "品红",
            "冰蓝",
            "橙焰",
            "柠檬黄",
            "银辉",
            "墨黑",
            "石板",
            "藏青字"
        };

        public const int TransparentSurfaceCount = 12;

        public static readonly string[] SurfaceNames =
        {
            "夜绿",
            "石墨",
            "藏青",
            "青钢",
            "紫夜",
            "琥珀",
            "酒红",
            "深空",
            "玫瑰",
            "森林",
            "午夜",
            "铜锈",
            "米白",
            "纸白",
            "浅灰",
            "雾蓝",
            "浅杏",
            "薄荷绿",
            "淡紫",
            "珍珠白"
        };

        public static bool IsOpaqueSurface(int surfaceTone)
        {
            return surfaceTone >= TransparentSurfaceCount;
        }

        public static readonly string[] FontNames =
        {
            "赛博",
            "锐线",
            "终端",
            "几何",
            "仪表",
            "霓虹",
            "冰岛",
            "电刻"
        };

        public static readonly string[] ScaleNames =
        {
            "迷你",
            "小",
            "标准",
            "大号",
            "很大",
            "超大"
        };
    }

    internal static class ClockMenuChrome
    {
        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");
        public static readonly Brush Surface = Solid(255, 28, 30, 34);
        public static readonly Brush Foreground = Solid(255, 236, 238, 242);
        public static readonly Brush Border = Solid(255, 72, 76, 84);
        public static readonly Brush Separator = Solid(255, 58, 62, 70);
        public static readonly Brush Highlight = Solid(255, 52, 90, 148);
        public static readonly Brush ItemBackground = Brushes.Transparent;

        private static SolidColorBrush Solid(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }
    }

    internal static class ClockTypography
    {
        public const double TimeSize = 30.0;
        public const double YearSize = 22.0;
        public const double DateSize = 17.0;
        public const double SecondsSize = 16.0;
        public const double PeriodSize = 11.0;

        public static FontFamily Create(int fontMode)
        {
            switch (fontMode)
            {
                case 1:
                    return FirstAvailable(
                        FileFamily("Orbitron-SemiBold.ttf", "Orbitron"),
                        FileFamily("Oxanium-SemiBold.ttf", "Oxanium"),
                        new FontFamily("Bahnschrift, Cascadia Mono, Consolas"));
                case 2:
                    return FirstAvailable(
                        FileFamily("ShareTechMono-Regular.ttf", "Share Tech Mono"),
                        new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"));
                case 3:
                    return FirstAvailable(
                        new FontFamily("Bahnschrift"),
                        FileFamily("Exo2-SemiBold.ttf", "Exo 2"),
                        new FontFamily("Segoe UI Semibold, Consolas"));
                case 4:
                    return FirstAvailable(
                        FileFamily("Rajdhani-SemiBold.ttf", "Rajdhani"),
                        FileFamily("Exo2-SemiBold.ttf", "Exo 2"),
                        new FontFamily("Bahnschrift, Segoe UI Semibold, Consolas"));
                case 5:
                    return FirstAvailable(
                        FileFamily("Audiowide-Regular.ttf", "Audiowide"),
                        FileFamily("Michroma-Regular.ttf", "Michroma"),
                        FileFamily("Orbitron-SemiBold.ttf", "Orbitron"),
                        new FontFamily("Bahnschrift, Consolas"));
                case 6:
                    return FirstAvailable(
                        FileFamily("Iceland-Regular.ttf", "Iceland"),
                        FileFamily("Electrolize-Regular.ttf", "Electrolize"),
                        new FontFamily("Bahnschrift, Consolas"));
                case 7:
                    return FirstAvailable(
                        FileFamily("Electrolize-Regular.ttf", "Electrolize"),
                        FileFamily("Michroma-Regular.ttf", "Michroma"),
                        FileFamily("Orbitron-SemiBold.ttf", "Orbitron"),
                        new FontFamily("Bahnschrift, Consolas"));
                default:
                    return FirstAvailable(
                        FileFamily("Oxanium-SemiBold.ttf", "Oxanium"),
                        FileFamily("Orbitron-SemiBold.ttf", "Orbitron"),
                        new FontFamily("Bahnschrift, Cascadia Mono, Consolas"));
            }
        }

        public static FontStretch Stretch(int fontMode)
        {
            if (fontMode == 1 || fontMode == 5)
            {
                return FontStretches.SemiCondensed;
            }

            return FontStretches.Normal;
        }

        public static FontWeight TimeWeight(int fontMode)
        {
            return (fontMode == 2 || fontMode == 6 || fontMode == 7)
                ? FontWeights.Medium
                : FontWeights.SemiBold;
        }

        public static FontWeight DateWeight(int fontMode)
        {
            return FontWeights.SemiBold;
        }

        public static void Apply(TextBlock text, int fontMode, double size, FontWeight weight)
        {
            text.FontFamily = Create(fontMode);
            text.FontStretch = Stretch(fontMode);
            text.FontWeight = weight;
            text.FontSize = size;
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            Typography.SetNumeralStyle(text, FontNumeralStyle.Lining);
        }

        public static void ApplyRun(Run run, int fontMode)
        {
            run.FontFamily = Create(fontMode);
            run.FontStretch = Stretch(fontMode);
        }

        private static FontFamily FirstAvailable(params FontFamily[] families)
        {
            for (int index = 0; index < families.Length; index++)
            {
                if (families[index] != null)
                {
                    return families[index];
                }
            }

            return new FontFamily("Consolas");
        }

        private static FontFamily FileFamily(string fileName, string familyName)
        {
            string[] roots =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts"),
                SettingsStore.FolderPath,
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "fonts"))
            };

            for (int index = 0; index < roots.Length; index++)
            {
                string path = Path.Combine(roots[index], fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    return new FontFamily(new Uri(path), "./#" + familyName);
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
