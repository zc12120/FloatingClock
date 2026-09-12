using System;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    // Desktop coordinates are physical pixels, even when monitors have different DPI.
    internal static class DisplayGeometry
    {
        public static Forms.Screen ScreenAt(double left, double top)
        {
            if (!IsValidCoordinate(left) || !IsValidCoordinate(top)) return Forms.Screen.PrimaryScreen;
            return Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(left), (int)Math.Round(top)));
        }

        public static Rect WorkAreaAt(double left, double top)
        {
            System.Drawing.Rectangle area = ScreenAt(left, top).WorkingArea;
            return new Rect(area.Left, area.Top, area.Width, area.Height);
        }

        public static double PrimaryScale
        {
            get
            {
                System.Drawing.Rectangle bounds = Forms.Screen.PrimaryScreen.Bounds;
                return ScaleAt(bounds.Left, bounds.Top);
            }
        }

        public static double ScaleAt(double left, double top)
        {
            if (!IsValidCoordinate(left) || !IsValidCoordinate(top))
            {
                System.Drawing.Rectangle primary = Forms.Screen.PrimaryScreen.Bounds;
                left = primary.Left;
                top = primary.Top;
            }

            try
            {
                NativePoint point = new NativePoint { X = (int)Math.Round(left), Y = (int)Math.Round(top) };
                IntPtr monitor = MonitorFromPoint(point, 2);
                uint x, y;
                if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0) return x / 96.0;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            IntPtr dc = GetDC(IntPtr.Zero);
            try { return dc == IntPtr.Zero ? 1.0 : Math.Max(96, GetDeviceCaps(dc, 88)) / 96.0; }
            finally { if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc); }
        }

        public static double ScaleForWindow(IntPtr handle, double left, double top)
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi > 0) return dpi / 96.0;
            }
            catch (EntryPointNotFoundException) { }
            return ScaleAt(left, top);
        }

        public static Rect[] WorkAreas()
        {
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            Rect[] areas = new Rect[screens.Length];
            for (int i = 0; i < screens.Length; i++)
            {
                System.Drawing.Rectangle area = screens[i].WorkingArea;
                areas[i] = new Rect(area.Left, area.Top, area.Width, area.Height);
            }
            return areas;
        }

        private static bool IsValidCoordinate(double value)
        {
            return ClockSettings.IsValidCoordinate(value);
        }

        public static Point Clamp(Rect window, Rect[] workAreas)
        {
            if (workAreas == null || workAreas.Length == 0)
                throw new ArgumentException("At least one working area is required.", "workAreas");
            if (window.IsEmpty || double.IsNaN(window.Width) || double.IsInfinity(window.Width)
                || double.IsNaN(window.Height) || double.IsInfinity(window.Height))
                throw new ArgumentException("Window dimensions must be finite.", "window");
            Point best = new Point();
            double bestDistance = double.PositiveInfinity;
            bool found = false;
            foreach (Rect area in workAreas)
            {
                if (area.IsEmpty || !IsValidCoordinate(area.Left) || !IsValidCoordinate(area.Top)
                    || !IsValidCoordinate(area.Right) || !IsValidCoordinate(area.Bottom)) continue;
                double sourceLeft = IsValidCoordinate(window.Left) ? window.Left : area.Left;
                double sourceTop = IsValidCoordinate(window.Top) ? window.Top : area.Top;
                double left = Math.Max(area.Left, Math.Min(sourceLeft, area.Right - window.Width));
                double top = Math.Max(area.Top, Math.Min(sourceTop, area.Bottom - window.Height));
                double dx = left - sourceLeft;
                double dy = top - sourceTop;
                double distance = (dx * dx) + (dy * dy);
                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestDistance = distance;
                    best = new Point(left, top);
                }
            }
            if (!found) throw new ArgumentException("No finite working area is available.", "workAreas");
            return best;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr dc, int index);
    }
}
