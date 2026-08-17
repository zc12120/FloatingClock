using System;
using System.Drawing;
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    internal static class LayeredDragProof
    {
        public static int Run()
        {
            Forms.Form backdrop = null;
            LayeredSurface displaySurface = null;
            LayeredSurface hitSurface = null;
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
                backdrop.Show();
                backdrop.Refresh();
                Forms.Application.DoEvents();

                const int width = 180;
                const int height = 64;
                int left = backdrop.Left + 40;
                int top = backdrop.Top + 80;

                displaySurface = new LayeredSurface(null);
                displaySurface.Create(true, LayeredSurface.DisplayClassName);
                displaySurface.SetClickThrough(true);
                if (!displaySurface.PresentSolid(width, height, left, top, 120, 16, 28, 34))
                {
                    return 31;
                }

                hitSurface = new LayeredSurface(null);
                hitSurface.Create(true, LayeredSurface.HitClassName);
                hitSurface.SetClickThrough(false);
                if (!hitSurface.PresentSolid(width, height, left, top, 1, 0, 0, 0))
                {
                    return 34;
                }

                hitSurface.SetTopmost(true);
                displaySurface.SetTopmost(true);
                Forms.Application.DoEvents();

                long displayStyle = NativeMethods.GetWindowLong(
                    displaySurface.Handle,
                    NativeMethods.ExtendedStyleIndex).ToInt64();
                long hitStyle = NativeMethods.GetWindowLong(
                    hitSurface.Handle,
                    NativeMethods.ExtendedStyleIndex).ToInt64();
                if (displaySurface.Handle == IntPtr.Zero
                    || hitSurface.Handle == IntPtr.Zero
                    || displaySurface.Handle == hitSurface.Handle
                    || (displayStyle & NativeMethods.TransparentStyle) == 0
                    || (hitStyle & NativeMethods.TransparentStyle) != 0)
                {
                    return 35;
                }

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
                    hitSurface.MovePixels(movedLeft, top);
                    displaySurface.MovePixels(movedLeft, top);
                    Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(25);
                    double luma = SampleLuma(movedLeft + 20, top + 16, width - 40, height - 32);
                    UpdateRange(ref dragMin, ref dragMax, luma);
                }

                if (dragMax - dragMin > 10.0)
                {
                    return 32;
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
                if (displaySurface != null)
                {
                    displaySurface.Dispose();
                }

                if (hitSurface != null)
                {
                    hitSurface.Dispose();
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
    }
}
