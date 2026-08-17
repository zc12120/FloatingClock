using System;
using System.Globalization;

namespace FloatingClock
{
    internal static class ClockLayout
    {
        public const double DesignHeight = 68.0;
        public const double DateColumnWidth = 44.0;
        public const double CornerRadius = 6.0;
        public const double DividerWidth = 1.0;
        public const double TimeColumnBase = 108.0;
        public const double TimeColumnWithSeconds = 138.0;
        public const double PeriodExtra = 24.0;
        public const double NoDatePadding = 14.0;

        public static readonly double[] Scales = { 0.72, 0.88, 1.00, 1.24, 1.52, 1.88 };

        public static double TimeColumnWidth(bool showSeconds, bool use24Hour)
        {
            double width = showSeconds ? TimeColumnWithSeconds : TimeColumnBase;
            if (!use24Hour)
            {
                width += PeriodExtra;
            }

            return width;
        }

        public static double DesignWidth(bool showDate, bool showSeconds, bool use24Hour)
        {
            double timeWidth = TimeColumnWidth(showSeconds, use24Hour);
            if (showDate)
            {
                return DateColumnWidth + DividerWidth + timeWidth + DividerWidth + DateColumnWidth;
            }

            return timeWidth + (NoDatePadding * 2.0);
        }

        public static double ScaleFactor(int scaleMode)
        {
            if (scaleMode < 0 || scaleMode >= Scales.Length)
            {
                return Scales[1];
            }

            return Scales[scaleMode];
        }
    }

    internal static class ClockFormatter
    {
        private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;

        public static string Hour(DateTime value, bool use24Hour)
        {
            return value.ToString(use24Hour ? "HH" : "hh", DisplayCulture);
        }

        public static string Minute(DateTime value)
        {
            return value.ToString("mm", DisplayCulture);
        }

        public static string SecondsSuffix(DateTime value, bool showSeconds)
        {
            return showSeconds ? value.ToString(":ss", DisplayCulture) : string.Empty;
        }

        public static string Period(DateTime value, bool use24Hour)
        {
            return use24Hour ? string.Empty : value.ToString("tt", DisplayCulture).ToUpperInvariant();
        }

        public static string Year(DateTime value)
        {
            return value.ToString("yy", DisplayCulture);
        }

        public static string Month(DateTime value)
        {
            return value.ToString("MM", DisplayCulture);
        }

        public static string Day(DateTime value)
        {
            return value.ToString("dd", DisplayCulture);
        }

        public static string TrayTime(DateTime value)
        {
            return value.ToString("HH:mm", DisplayCulture);
        }
    }

    internal static class OpacityPresets
    {
        public const double Opaque = 1.0;
        public const double Soft = 0.85;
        public const double Faint = 0.72;

        public static byte SurfaceAlpha(double opacity)
        {
            double value = Normalize(opacity);
            double t = (value - 0.70) / 0.30;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            return (byte)Math.Round(118.0 + (t * 80.0));
        }

        public static bool Matches(double value, double preset)
        {
            return Math.Abs(value - preset) < 0.02;
        }

        public static double Normalize(double value)
        {
            if (Matches(value, Opaque) || Matches(value, Soft) || Matches(value, Faint))
            {
                return Matches(value, Soft) ? Soft : (Matches(value, Faint) ? Faint : Opaque);
            }

            if (value < 0.70 || value > 1.0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                return Opaque;
            }

            return value;
        }
    }
}
