using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingClock
{
    internal sealed class LayeredSurface : IDisposable
    {
        public const string DisplayClassName = "FloatingClock.LayeredSurface";
        private const int WindowPopup = unchecked((int)0x80000000);
        private const int ExtendedTopmost = 0x00000008;
        private const int ShowNoActivate = 4;
        private const int HideWindow = 0;
        private const int MouseMoveMessage = 0x0200;
        private const int LeftButtonDownMessage = 0x0201;
        private const int LeftButtonUpMessage = 0x0202;
        private const int RightButtonUpMessage = 0x0205;
        private const int SetCursorMessage = 0x0020;
        private const int DestroyMessage = 0x0002;
        private const int PaintMessage = 0x000F;
        private const int NcPaintMessage = 0x0085;
        private const int NullBrush = 5;
        private const int LeftButtonFlag = 0x0001;
        private const int ArrowCursor = 32512;
        private const int SizeAllCursor = 32646;
        private const byte SourceOver = 0;
        private const byte SourceAlpha = 1;
        private const uint UpdateAlpha = 2;
        private const uint DibRgb = 0;
        private static readonly IntPtr NoTopmostInsertAfter = new IntPtr(-2);

        private readonly Window host;
        private readonly WndProc wndProc;
        private string className;
        private IntPtr windowHandle;
        private bool classRegistered;
        private bool dragging;
        private bool disposed;
        private int dragOffsetX;
        private int dragOffsetY;
        private int pixelWidth;
        private int pixelHeight;
        private int lastLeft;
        private int lastTop;
        private bool hasLayer;
        private bool shown;
        private IntPtr bits;
        private IntPtr section;
        private IntPtr memoryDc;
        private IntPtr oldBitmap;

        public Action<double, double> Moved;
        public Action MoveFinished;
        public Action MenuRequested;

        public bool Locked { get; set; }

        public bool IsDragging
        {
            get { return dragging; }
        }

        public LayeredSurface(Window host)
        {
            this.host = host;
            wndProc = HandleMessage;
        }

        public IntPtr Handle
        {
            get { return windowHandle; }
        }

        public void Create(bool topmost)
        {
            Create(topmost, DisplayClassName);
        }

        public void Create(bool topmost, string windowClassName)
        {
            if (windowHandle != IntPtr.Zero)
            {
                return;
            }

            className = string.IsNullOrEmpty(windowClassName) ? DisplayClassName : windowClassName;
            WndClassEx windowClass = new WndClassEx();
            windowClass.Size = Marshal.SizeOf(typeof(WndClassEx));
            windowClass.Procedure = wndProc;
            windowClass.Instance = GetModuleHandle(null);
            windowClass.Cursor = LoadCursor(IntPtr.Zero, new IntPtr(SizeAllCursor));
            windowClass.Background = GetStockObject(NullBrush);
            windowClass.ClassName = className;
            ushort atom = RegisterClassEx(ref windowClass);
            if (atom == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1410)
                {
                    return;
                }
            }
            else
            {
                classRegistered = true;
            }

            int style = (int)(NativeMethods.ToolWindowStyle | NativeMethods.NoActivateStyle | NativeMethods.LayeredStyle);
            if (topmost)
            {
                style |= ExtendedTopmost;
            }

            windowHandle = CreateWindowEx(
                style,
                className,
                string.Empty,
                WindowPopup,
                0,
                0,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.DisableTransitions(windowHandle);
            DwmGlass.NeutralizeHover(windowHandle);
            SetWindowTheme(windowHandle, string.Empty, string.Empty);
        }

        public void Present(Visual visual, double dipWidth, double dipHeight, double dipLeft, double dipTop)
        {
            if (disposed || dragging || windowHandle == IntPtr.Zero || visual == null || dipWidth < 1 || dipHeight < 1)
            {
                return;
            }

            Point scale = DeviceScale();
            int width = Math.Max(1, (int)Math.Round(dipWidth * scale.X));
            int height = Math.Max(1, (int)Math.Round(dipHeight * scale.Y));
            int left = (int)Math.Round(dipLeft * scale.X);
            int top = (int)Math.Round(dipTop * scale.Y);

            FrameworkElement element = visual as FrameworkElement;
            if (element != null)
            {
                if (element.ActualWidth < 1 || element.ActualHeight < 1)
                {
                    element.Measure(new Size(dipWidth, dipHeight));
                    element.Arrange(new Rect(0, 0, dipWidth, dipHeight));
                    element.UpdateLayout();
                }
            }

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                width,
                height,
                96.0 * scale.X,
                96.0 * scale.Y,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            if (!EnsureBuffer(width, height))
            {
                return;
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);
            Marshal.Copy(pixels, 0, bits, pixels.Length);

            PushLayer(left, top);
        }

        public bool PresentSolid(int width, int height, int left, int top, byte alpha, byte red, byte green, byte blue)
        {
            if (disposed || windowHandle == IntPtr.Zero || width < 1 || height < 1)
            {
                return false;
            }

            if (!EnsureBuffer(width, height))
            {
                return false;
            }

            byte preRed = (byte)(red * alpha / 255);
            byte preGreen = (byte)(green * alpha / 255);
            byte preBlue = (byte)(blue * alpha / 255);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = preBlue;
                pixels[index + 1] = preGreen;
                pixels[index + 2] = preRed;
                pixels[index + 3] = alpha;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);
            return PushLayer(left, top);
        }

        public void MoveTo(double dipLeft, double dipTop)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            Point scale = DeviceScale();
            MovePixels(
                (int)Math.Round(dipLeft * scale.X),
                (int)Math.Round(dipTop * scale.Y));
        }

        public void MovePixels(int left, int top)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (PushLayer(left, top))
            {
                return;
            }

            PointInt destination = new PointInt(left, top);
            IntPtr destinationPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PointInt)));
            try
            {
                Marshal.StructureToPtr(destination, destinationPointer, false);
                UpdateLayeredWindowRaw(
                    windowHandle,
                    IntPtr.Zero,
                    destinationPointer,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    0);
            }
            finally
            {
                Marshal.FreeHGlobal(destinationPointer);
            }
        }

        public void SetVisible(bool visible)
        {
            if (windowHandle == IntPtr.Zero || shown == visible)
            {
                return;
            }

            shown = visible;
            if (!visible)
            {
                ShowWindow(windowHandle, HideWindow);
                return;
            }

            ShowWindow(windowHandle, ShowNoActivate);
            Repush();
        }

        public void SetTopmost(bool topmost)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                windowHandle,
                topmost ? NativeMethods.TopmostInsertAfter : NoTopmostInsertAfter,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoRedraw);
            Repush();
        }

        public void SetClickThrough(bool enabled)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            long current = NativeMethods.GetWindowLong(windowHandle, NativeMethods.ExtendedStyleIndex).ToInt64();
            long style = current | NativeMethods.LayeredStyle | NativeMethods.ToolWindowStyle | NativeMethods.NoActivateStyle;
            if (enabled)
            {
                style |= NativeMethods.TransparentStyle;
            }
            else
            {
                style &= ~NativeMethods.TransparentStyle;
            }

            if (style != current)
            {
                NativeMethods.SetWindowLong(windowHandle, NativeMethods.ExtendedStyleIndex, new IntPtr(style));
                Repush();
            }
        }

        public void Repush()
        {
            if (hasLayer)
            {
                PushLayer(lastLeft, lastTop);
            }
        }

        public void BringForward()
        {
            NativeMethods.KeepTopmost(windowHandle);
            Repush();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseBuffer();
            if (windowHandle != IntPtr.Zero)
            {
                DestroyWindow(windowHandle);
                windowHandle = IntPtr.Zero;
            }

            if (classRegistered && !string.IsNullOrEmpty(className))
            {
                UnregisterClass(className, GetModuleHandle(null));
                classRegistered = false;
            }
        }

        private bool EnsureBuffer(int width, int height)
        {
            if (section != IntPtr.Zero && pixelWidth == width && pixelHeight == height)
            {
                return true;
            }

            ReleaseBuffer();

            BitmapInfo info = new BitmapInfo();
            info.Size = Marshal.SizeOf(typeof(BitmapInfo));
            info.Width = width;
            info.Height = -height;
            info.Planes = 1;
            info.BitCount = 32;
            info.Compression = (int)DibRgb;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                section = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
                if (section == IntPtr.Zero)
                {
                    return false;
                }

                memoryDc = CreateCompatibleDC(screenDc);
                oldBitmap = SelectObject(memoryDc, section);
                pixelWidth = width;
                pixelHeight = height;
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private bool PushLayer(int left, int top)
        {
            if (windowHandle == IntPtr.Zero || memoryDc == IntPtr.Zero || pixelWidth < 1 || pixelHeight < 1)
            {
                return false;
            }

            PointInt destination = new PointInt(left, top);
            SizeInt size = new SizeInt(pixelWidth, pixelHeight);
            PointInt source = new PointInt(0, 0);
            BlendFunction blend = new BlendFunction
            {
                BlendOp = SourceOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = SourceAlpha
            };

            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                bool ok = UpdateLayeredWindow(
                    windowHandle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    UpdateAlpha);
                if (ok)
                {
                    lastLeft = left;
                    lastTop = top;
                    hasLayer = true;
                    shown = true;
                }

                return ok;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void ReleaseBuffer()
        {
            if (memoryDc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                    oldBitmap = IntPtr.Zero;
                }

                DeleteDC(memoryDc);
                memoryDc = IntPtr.Zero;
            }

            if (section != IntPtr.Zero)
            {
                DeleteObject(section);
                section = IntPtr.Zero;
                bits = IntPtr.Zero;
            }

            pixelWidth = 0;
            pixelHeight = 0;
        }

        private IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == SetCursorMessage)
            {
                SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(Locked ? ArrowCursor : SizeAllCursor)));
                return new IntPtr(1);
            }

            if (message == NativeMethods.EraseBackgroundMessage || message == PaintMessage || message == NcPaintMessage)
            {
                if (message == PaintMessage)
                {
                    PaintStruct paint;
                    IntPtr context = BeginPaint(handle, out paint);
                    if (context != IntPtr.Zero)
                    {
                        EndPaint(handle, ref paint);
                    }
                }

                return new IntPtr(1);
            }

            if (message == NativeMethods.MouseActivateMessage)
            {
                return new IntPtr(NativeMethods.MouseActivateNoActivate);
            }

            if (message == NativeMethods.NcActivateMessage || message == NativeMethods.ActivateMessage)
            {
                return new IntPtr(1);
            }

            if (message == LeftButtonDownMessage)
            {
                if (!Locked)
                {
                    NativeRect bounds;
                    GetWindowRect(handle, out bounds);
                    PointInt cursor;
                    GetCursorPos(out cursor);
                    dragging = true;
                    dragOffsetX = cursor.X - bounds.Left;
                    dragOffsetY = cursor.Y - bounds.Top;
                    SetCapture(handle);
                }

                return IntPtr.Zero;
            }

            if (message == MouseMoveMessage)
            {
                if (dragging && ((wParam.ToInt64() & LeftButtonFlag) != 0))
                {
                    PointInt cursor;
                    GetCursorPos(out cursor);
                    int left = cursor.X - dragOffsetX;
                    int top = cursor.Y - dragOffsetY;
                    MovePixels(left, top);
                    Point dip = PixelToDip(left, top);
                    Action<double, double> moved = Moved;
                    if (moved != null)
                    {
                        moved(dip.X, dip.Y);
                    }
                }

                return IntPtr.Zero;
            }

            if (message == LeftButtonUpMessage)
            {
                if (dragging)
                {
                    dragging = false;
                    ReleaseCapture();
                    Action finished = MoveFinished;
                    if (finished != null)
                    {
                        finished();
                    }
                }

                return IntPtr.Zero;
            }

            if (message == RightButtonUpMessage)
            {
                Action menu = MenuRequested;
                if (menu != null)
                {
                    menu();
                }

                return IntPtr.Zero;
            }

            if (message == DestroyMessage)
            {
                windowHandle = IntPtr.Zero;
            }

            return DefWindowProc(handle, message, wParam, lParam);
        }

        private Point DeviceScale()
        {
            if (host == null)
            {
                return new Point(1, 1);
            }

            PresentationSource source = PresentationSource.FromVisual(host);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix matrix = source.CompositionTarget.TransformToDevice;
                return new Point(matrix.M11, matrix.M22);
            }

            return new Point(1, 1);
        }

        private Point PixelToDip(int x, int y)
        {
            Point scale = DeviceScale();
            return new Point(x / scale.X, y / scale.Y);
        }

        private delegate IntPtr WndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassEx
        {
            public int Size;
            public int Style;
            public WndProc Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointInt
        {
            public int X;
            public int Y;

            public PointInt(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeInt
        {
            public int Cx;
            public int Cy;

            public SizeInt(int cx, int cy)
            {
                Cx = cx;
                Cy = cy;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ColorsUsed;
            public int ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential, Size = 72)]
        private struct PaintStruct
        {
            public IntPtr Context;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out PointInt point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr cursor);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string module);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int objectId);

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr handle, out PaintStruct paint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr handle, ref PaintStruct paint);

        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
        private static extern bool UpdateLayeredWindowRaw(
            IntPtr handle,
            IntPtr destinationContext,
            IntPtr destination,
            IntPtr size,
            IntPtr sourceContext,
            IntPtr source,
            uint colorKey,
            IntPtr blend,
            uint flags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr deviceContext,
            ref BitmapInfo info,
            uint usage,
            out IntPtr bits,
            IntPtr section,
            uint offset);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(
            IntPtr handle,
            IntPtr destinationContext,
            ref PointInt destination,
            ref SizeInt size,
            IntPtr sourceContext,
            ref PointInt source,
            uint colorKey,
            ref BlendFunction blend,
            uint flags);
    }
}
