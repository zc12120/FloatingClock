using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FloatingClock
{
    // Presets use the existing settings fields, so saved positions and interaction
    // preferences survive a theme change and older settings need no migration.
    internal static class ClockThemePresets
    {
        public static readonly string[] Names = { "Reload 蓝", "深夜青", "月光白", "SMTVV 玉金" };
        public static readonly string[] Captions = { "RELOAD", "DARK HOUR", "MOONLIGHT", "SMTVV" };
        public static readonly string[] Descriptions = {
            "冷白数字 / 钴蓝背景 / 仪表字体",
            "电光青数字 / 深空背景 / 仪表字体",
            "藏青数字 / 雾蓝实色 / 仪表字体",
            "象牙白数字 / 玉青背景 / 图鉴字体"
        };
        private static readonly int[] Inks = { 2, 3, 13, 14 };
        private static readonly int[] Surfaces = { 2, 7, 15, ClockLooks.SmtvvSurface };
        private static readonly int[] Fonts = { 4, 4, 4, 8 };

        public static int Match(ClockSettings settings)
        {
            for (int i = 0; i < Names.Length; i++)
                if (settings.ThemeMode == Inks[i] && settings.SurfaceTone == Surfaces[i]
                    && settings.FontMode == Fonts[i]) return i;
            return -1;
        }

        public static bool Apply(ClockSettings settings, int index)
        {
            if (index < 0 || index >= Names.Length) throw new ArgumentOutOfRangeException("index");
            if (Match(settings) == index) return false;
            settings.ThemeMode = Inks[index];
            settings.SurfaceTone = Surfaces[index];
            settings.FontMode = Fonts[index];
            return true;
        }

        public static ClockPalette Palette(int index)
        {
            return ClockPalette.Create(Inks[index], Surfaces[index]);
        }
    }

    // Retained vector artwork is composited by the same layered surface as the
    // digits. No extra HWND, continuous animation, or desktop blur is involved.
    internal sealed class ReloadClockChrome : FrameworkElement
    {
        private ClockPalette palette;
        private bool showDate;
        private string caption;
        private readonly Typeface typeface = new Typeface(ClockTypography.Create(4),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public ReloadClockChrome() { IsHitTestVisible = false; }

        public void Configure(ClockPalette value, bool date, string themeCaption)
        {
            palette = value;
            showDate = date;
            caption = themeCaption;
            InvalidateVisual();
        }

        public static Geometry Silhouette(double width, double height)
        {
            return Polygon(new Point(11, 0), new Point(width, 0), new Point(width, height - 11),
                new Point(width - 11, height), new Point(0, height), new Point(0, 11));
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            if (palette == null || ActualWidth <= 0) return;
            double w = ActualWidth;
            double h = ActualHeight;
            Color accent = palette.IsOpaque ? palette.BorderTint : Color.FromRgb(67, 222, 255);
            Brush bright = Solid(accent, 230);
            Brush quiet = Solid(accent, 100);
            Brush label = palette.IsOpaque ? palette.TimeSecondary : Solid(Color.FromRgb(167, 221, 246), 255);

            // A clipped blue wing and offset slashes carry the Reload silhouette.
            drawing.DrawGeometry(Solid(palette.BorderTint, 30), null, Polygon(
                new Point(w - 33, 0), new Point(w, 0), new Point(w, h), new Point(w - 67, h)));
            drawing.DrawGeometry(bright, null, Polygon(new Point(11, 0), new Point(w * .58, 0),
                new Point(w * .58 - 3, 2), new Point(9, 2)));
            drawing.DrawGeometry(quiet, null, Polygon(new Point(0, 17), new Point(2, 15),
                new Point(2, h - 8), new Point(0, h - 6)));
            drawing.DrawGeometry(bright, null, Polygon(new Point(w - 31, h - 3), new Point(w - 14, h - 3),
                new Point(w - 17, h), new Point(w - 34, h)));
            drawing.DrawGeometry(bright, null, Polygon(new Point(w - 11, h - 10), new Point(w - 3, h - 18),
                new Point(w - 3, h - 13), new Point(w - 11, h - 5)));

            double center = w / 2;
            DrawLabel(drawing, "LOCAL TIME", center, 4, 6.5, label, true);
            DrawLabel(drawing, caption ?? "RELOAD", center, h - 11, 6.5, label, true);
            if (showDate)
            {
                DrawLabel(drawing, "YEAR", ClockLayout.DateColumnWidth / 2, 8, 6, label, true);
                drawing.DrawLine(new Pen(quiet, 1), new Point(17, h - 14), new Point(21, h - 18));
                drawing.DrawLine(new Pen(quiet, 1), new Point(21, h - 14), new Point(25, h - 18));
            }
        }

        private void DrawLabel(DrawingContext drawing, string text, double x, double y, double size, Brush brush, bool centered)
        {
            FormattedText label = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, size, brush, 1.0);
            drawing.DrawText(label, new Point(centered ? x - label.Width / 2 : x, y));
        }

        private static SolidColorBrush Solid(Color color, byte alpha)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static StreamGeometry Polygon(params Point[] points)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) context.LineTo(points[i], true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
