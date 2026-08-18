using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    internal static class LayeredDragProof
    {
        public static int Run()
        {
            Forms.Form backdrop = null;
            Forms.Panel contrastPanel = null;
            LayeredSurface surface = null;
            Point cursorPosition = Forms.Cursor.Position;
            try
            {
                backdrop = new Forms.Form
                {
                    FormBorderStyle = Forms.FormBorderStyle.None,
                    ShowInTaskbar = false,
                    ShowIcon = false,
                    StartPosition = Forms.FormStartPosition.Manual,
                    BackColor = Color.White,
                    TopMost = true,
                    Bounds = new Rectangle(120, 120, 520, 260)
                };
                contrastPanel = new Forms.Panel
                {
                    BackColor = Color.Black,
                    Bounds = new Rectangle(130, 0, 390, 260)
                };
                backdrop.Controls.Add(contrastPanel);
                backdrop.Show();
                backdrop.Refresh();
                Forms.Application.DoEvents();

                const int width = 180;
                const int height = 64;
                int left = backdrop.Left + 40;
                int top = backdrop.Top + 80;

                surface = new LayeredSurface(null);
                surface.Create(true, LayeredSurface.DisplayClassName);
                surface.SetClickThrough(false);
                if (!surface.PresentSolid(width, height, left, top, 120, 16, 28, 34))
                {
                    return 31;
                }

                surface.SetTopmost(true);
                Forms.Application.DoEvents();

                double lightBackdropLuma = SampleLuma(left + 30, top + 20, 20, 20);
                double darkBackdropLuma = SampleLuma(left + 130, top + 20, 20, 20);
                if (lightBackdropLuma - darkBackdropLuma < 60.0)
                {
                    return 37;
                }

                contrastPanel.Visible = false;
                backdrop.Refresh();
                Forms.Application.DoEvents();

                long surfaceStyle = NativeMethods.GetWindowLong(
                    surface.Handle,
                    NativeMethods.ExtendedStyleIndex).ToInt64();
                if (surface.Handle == IntPtr.Zero
                    || (surfaceStyle & NativeMethods.TransparentStyle) != 0)
                {
                    return 35;
                }

                surface.SetClickThrough(true);
                surfaceStyle = NativeMethods.GetWindowLong(
                    surface.Handle,
                    NativeMethods.ExtendedStyleIndex).ToInt64();
                if ((surfaceStyle & NativeMethods.TransparentStyle) == 0)
                {
                    return 34;
                }

                surface.SetClickThrough(false);

                double first = SampleLuma(left + 20, top + 16, width - 40, height - 32);
                double mouseMin = first;
                double mouseMax = first;
                for (int step = 0; step <= 12; step++)
                {
                    Forms.Cursor.Position = new Point(left + 10 + (step * 13), top + 32);
                    Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(25);
                    double luma = SampleLuma(left + 20, top + 16, width - 40, height - 32);
                    UpdateRange(ref mouseMin, ref mouseMax, luma);
                }

                if (mouseMax - mouseMin > 10.0)
                {
                    return 36;
                }

                double dragMin = first;
                double dragMax = first;
                for (int step = 1; step <= 12; step++)
                {
                    int movedLeft = left + (step * 16);
                    surface.MovePixels(movedLeft, top);
                    Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(25);
                    double luma = SampleLuma(movedLeft + 20, top + 16, width - 40, height - 32);
                    UpdateRange(ref dragMin, ref dragMax, luma);
                }

                if (dragMax - dragMin > 10.0)
                {
                    return 32;
                }

                int directLeft = left + (12 * 16);
                double inputLeft = double.NaN;
                double inputTop = double.NaN;
                bool inputFinished = false;
                surface.Moved = delegate(double movedLeft, double movedTop)
                {
                    inputLeft = movedLeft;
                    inputTop = movedTop;
                };
                surface.MoveFinished = delegate { inputFinished = true; };
                Forms.Cursor.Position = new Point(directLeft + 20, top + 20);
                SendMessage(surface.Handle, 0x0201, new IntPtr(1), IntPtr.Zero);
                Forms.Cursor.Position = new Point(directLeft + 44, top + 28);
                SendMessage(surface.Handle, 0x0200, new IntPtr(1), IntPtr.Zero);
                SendMessage(surface.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                if (!inputFinished
                    || Math.Abs(inputLeft - (directLeft + 24)) > 1.0
                    || Math.Abs(inputTop - (top + 8)) > 1.0)
                {
                    return 38;
                }

                return 0;
            }
            catch
            {
                return 33;
            }
            finally
            {
                Forms.Cursor.Position = cursorPosition;
                if (surface != null)
                {
                    surface.Dispose();
                }

                if (backdrop != null)
                {
                    backdrop.Close();
                    backdrop.Dispose();
                }
            }
        }

        private static void UpdateRange(ref double minimum, ref double maximum, double value)
        {
            if (value < minimum)
            {
                minimum = value;
            }

            if (value > maximum)
            {
                maximum = value;
            }
        }

        private static double SampleLuma(int left, int top, int width, int height)
        {
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
                }

                double total = 0;
                int count = 0;
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        total += (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
                        count++;
                    }
                }

                return count == 0 ? 0 : total / count;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);
    }
}
