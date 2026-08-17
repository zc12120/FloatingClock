using System;
using System.Windows;
using System.Windows.Media;

namespace FloatingClock
{
    internal sealed class ClockPalette
    {
        public Color SurfaceSheen { get; private set; }
        public Color SurfaceTint { get; private set; }
        public Color SurfaceDeep { get; private set; }
        public Color BorderTint { get; private set; }
        public Color DividerTint { get; private set; }
        public Brush TimeInk { get; private set; }
        public Brush TimeSecondary { get; private set; }
        public Brush DateInk { get; private set; }
        public bool IsOpaque { get; private set; }
        public Brush MenuSurface { get; private set; }
        public Brush MenuForeground { get; private set; }
        public Brush MenuBorder { get; private set; }

        public static ClockPalette Create(int themeMode, int surfaceTone)
        {
            ClockPalette palette = new ClockPalette();
            ApplySurface(palette, surfaceTone);
            ApplyInk(palette, themeMode);
            return palette;
        }

        private static void ApplySurface(ClockPalette palette, int surfaceTone)
        {
            switch (surfaceTone)
            {
                case 1:
                    Tone(palette, 62, 66, 74, 34, 38, 44, 16, 18, 22, 186, 192, 202, 132, 138, 148);
                    break;
                case 2:
                    Tone(palette, 24, 48, 92, 10, 28, 64, 4, 14, 38, 96, 156, 255, 56, 104, 188);
                    break;
                case 3:
                    Tone(palette, 12, 68, 78, 6, 42, 50, 3, 24, 30, 64, 214, 214, 32, 148, 156);
                    break;
                case 4:
                    Tone(palette, 62, 32, 92, 36, 16, 58, 20, 8, 36, 196, 132, 255, 132, 78, 188);
                    break;
                case 5:
                    Tone(palette, 78, 48, 18, 48, 28, 10, 28, 16, 6, 255, 176, 72, 188, 118, 42);
                    break;
                case 6:
                    Tone(palette, 92, 24, 36, 58, 12, 22, 32, 6, 12, 255, 96, 118, 176, 56, 72);
                    break;
                case 7:
                    Tone(palette, 18, 28, 52, 8, 12, 28, 2, 4, 14, 120, 160, 220, 72, 96, 140);
                    break;
                case 8:
                    Tone(palette, 86, 28, 48, 52, 14, 28, 28, 6, 16, 255, 132, 168, 176, 72, 104);
                    break;
                case 9:
                    Tone(palette, 18, 56, 28, 8, 34, 16, 4, 18, 8, 86, 196, 112, 42, 132, 68);
                    break;
                case 10:
                    Tone(palette, 36, 28, 86, 18, 12, 52, 8, 6, 28, 148, 132, 255, 88, 78, 176);
                    break;
                case 11:
                    Tone(palette, 86, 52, 28, 52, 30, 14, 28, 16, 8, 220, 156, 88, 156, 102, 56);
                    break;
                case 12:
                    Tone(palette, 255, 252, 246, 250, 246, 238, 236, 230, 218, 176, 164, 140, 196, 186, 168);
                    palette.IsOpaque = true;
                    break;
                case 13:
                    Tone(palette, 255, 255, 255, 248, 248, 248, 232, 232, 234, 168, 170, 176, 196, 198, 202);
                    palette.IsOpaque = true;
                    break;
                case 14:
                    Tone(palette, 244, 246, 248, 232, 234, 236, 214, 218, 222, 140, 146, 154, 168, 172, 178);
                    palette.IsOpaque = true;
                    break;
                case 15:
                    Tone(palette, 244, 250, 255, 228, 238, 248, 204, 220, 236, 120, 156, 196, 156, 180, 208);
                    palette.IsOpaque = true;
                    break;
                case 16:
                    Tone(palette, 255, 248, 236, 255, 242, 228, 236, 220, 196, 196, 156, 112, 212, 180, 140);
                    palette.IsOpaque = true;
                    break;
                case 17:
                    Tone(palette, 240, 252, 244, 228, 244, 234, 204, 228, 212, 96, 164, 128, 140, 188, 160);
                    palette.IsOpaque = true;
                    break;
                case 18:
                    Tone(palette, 250, 244, 255, 242, 236, 250, 224, 214, 236, 156, 132, 196, 180, 164, 208);
                    palette.IsOpaque = true;
                    break;
                case 19:
                    Tone(palette, 255, 255, 255, 255, 255, 255, 242, 242, 244, 180, 182, 188, 208, 210, 214);
                    palette.IsOpaque = true;
                    break;
                default:
                    Tone(palette, 16, 64, 36, 8, 40, 22, 4, 22, 12, 56, 198, 108, 36, 148, 78);
                    break;
            }
        }

        private static void Tone(
            ClockPalette palette,
            byte sheenR, byte sheenG, byte sheenB,
            byte tintR, byte tintG, byte tintB,
            byte deepR, byte deepG, byte deepB,
            byte borderR, byte borderG, byte borderB,
            byte divR, byte divG, byte divB)
        {
            palette.SurfaceSheen = Color.FromRgb(sheenR, sheenG, sheenB);
            palette.SurfaceTint = Color.FromRgb(tintR, tintG, tintB);
            palette.SurfaceDeep = Color.FromRgb(deepR, deepG, deepB);
            palette.BorderTint = Color.FromRgb(borderR, borderG, borderB);
            palette.DividerTint = Color.FromRgb(divR, divG, divB);
        }

        private static void ApplyInk(ClockPalette palette, int themeMode)
        {
            switch (themeMode)
            {
                case 1:
                    Ink(palette, 150, 248, 176, 78, 176, 112, 112, 220, 148);
                    break;
                case 2:
                    Ink(palette, 248, 250, 255, 198, 204, 214, 228, 232, 240);
                    break;
                case 3:
                    Ink(palette, 72, 236, 255, 40, 176, 214, 124, 232, 246);
                    break;
                case 4:
                    Ink(palette, 255, 186, 74, 214, 132, 42, 255, 204, 118);
                    break;
                case 5:
                    Ink(palette, 255, 92, 176, 214, 48, 122, 255, 148, 196);
                    break;
                case 6:
                    Ink(palette, 255, 72, 214, 196, 36, 168, 255, 132, 228);
                    break;
                case 7:
                    Ink(palette, 154, 220, 255, 86, 164, 220, 196, 232, 255);
                    break;
                case 8:
                    Ink(palette, 255, 132, 56, 220, 86, 28, 255, 168, 104);
                    break;
                case 9:
                    Ink(palette, 240, 232, 72, 196, 180, 32, 248, 240, 128);
                    break;
                case 10:
                    Ink(palette, 214, 220, 228, 156, 164, 176, 232, 236, 242);
                    break;
                case 11:
                    Ink(palette, 22, 24, 28, 72, 76, 84, 36, 40, 46);
                    break;
                case 12:
                    Ink(palette, 56, 62, 72, 96, 104, 116, 72, 80, 92);
                    break;
                case 13:
                    Ink(palette, 24, 52, 104, 48, 80, 140, 32, 64, 118);
                    break;
                default:
                    Ink(palette, 86, 255, 132, 48, 196, 96, 72, 236, 124);
                    break;
            }

            palette.MenuSurface = Solid(WithAlpha(palette.SurfaceDeep, 242));
        }

        public uint AccentColor(double opacity)
        {
            byte alpha = OpacityPresets.SurfaceAlpha(opacity);
            return ((uint)alpha << 24)
                | ((uint)SurfaceTint.B << 16)
                | ((uint)SurfaceTint.G << 8)
                | SurfaceTint.R;
        }

        private static void Ink(
            ClockPalette palette,
            byte timeR, byte timeG, byte timeB,
            byte secR, byte secG, byte secB,
            byte dateR, byte dateG, byte dateB)
        {
            palette.TimeInk = Brush(255, timeR, timeG, timeB);
            palette.TimeSecondary = Brush(255, secR, secG, secB);
            palette.DateInk = Brush(255, dateR, dateG, dateB);
            palette.MenuForeground = palette.TimeInk;
            palette.MenuBorder = Brush(140, dateR, dateG, dateB);
        }

        public Brush CreateOpaqueSurface()
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };
            brush.GradientStops.Add(new GradientStop(SurfaceSheen, 0.0));
            brush.GradientStops.Add(new GradientStop(SurfaceTint, 0.45));
            brush.GradientStops.Add(new GradientStop(SurfaceDeep, 1.0));
            brush.Freeze();
            return brush;
        }

        public Brush CreateSurface(double opacity)
        {
            if (IsOpaque)
            {
                return CreateOpaqueSurface();
            }

            return Solid(WithAlpha(SurfaceTint, OpacityPresets.SurfaceAlpha(opacity)));
        }

        public Brush CreateBorder(double opacity)
        {
            if (IsOpaque)
            {
                return Solid(BorderTint);
            }

            byte alpha = (byte)Math.Min(255, OpacityPresets.SurfaceAlpha(opacity) + 48);
            return Solid(WithAlpha(BorderTint, alpha));
        }

        public Brush CreateDivider(double opacity)
        {
            if (IsOpaque)
            {
                return Solid(DividerTint);
            }

            byte alpha = (byte)Math.Min(255, OpacityPresets.SurfaceAlpha(opacity) + 16);
            return Solid(WithAlpha(DividerTint, alpha));
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue)
        {
            return Solid(Color.FromArgb(alpha, red, green, blue));
        }

        private static SolidColorBrush Solid(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
