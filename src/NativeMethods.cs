using System;
using System.Runtime.InteropServices;

namespace FloatingClock
{
    internal static class NativeMethods
    {
        public const int ExtendedStyleIndex = -20;
        public const long TransparentStyle = 0x00000020L;
        public const long ToolWindowStyle = 0x00000080L;
        public const long LayeredStyle = 0x00080000L;
        public const long NoActivateStyle = 0x08000000L;
        public const uint LayeredColorKey = 0x00000001;
        public const uint LayeredAlpha = 0x00000002;
        public const uint ColorKeyRef = 0x00FF00FF;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoRedraw = 0x0008;
        public const uint SwpNoCopyBits = 0x0100;
        public const uint SwpNoSendChanging = 0x0400;
        public const uint SwpDeferErase = 0x2000;
        public const uint SwpFrameChanged = 0x0020;
        public const uint QuietMoveFlags = SwpNoSize | SwpNoActivate | SwpNoRedraw | SwpNoCopyBits | SwpNoSendChanging | SwpDeferErase;
        public const int WindowPosChangingMessage = 0x0046;
        public const int StyleChangingMessage = 0x007C;
        public const int NcHitTestMessage = 0x0084;
        public const int MouseActivateMessage = 0x0021;
        public const int NcActivateMessage = 0x0086;
        public const int ActivateMessage = 0x0006;
        public const int EraseBackgroundMessage = 0x0014;
        public const int MouseActivateNoActivate = 3;
        public const int HitTestClient = 1;
        public const int DwmTransitionsForcedDisabled = 3;
        public const int DwmCloakAttribute = 13;
        public static readonly IntPtr TopmostInsertAfter = new IntPtr(-1);
        public const int HotKeyMessage = 0x0312;
        public const int ClickThroughHotKeyId = 0x434C;
        public const uint AltModifier = 0x0001;
        public const uint ControlModifier = 0x0002;
        public const uint NoRepeatModifier = 0x4000;
        public const uint TKey = 0x54;

        public static IntPtr GetWindowLong(IntPtr handle, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, index)
                : new IntPtr(GetWindowLong32(handle, index));
        }

        public static IntPtr SetWindowLong(IntPtr handle, int index, IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(handle, index, value)
                : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr handle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

        [StructLayout(LayoutKind.Sequential)]
        private struct StyleStruct
        {
            public int StyleOld;
            public int StyleNew;
        }

        public static void KeepLayeredStyle(IntPtr stylePointer)
        {
            if (stylePointer == IntPtr.Zero)
            {
                return;
            }

            StyleStruct style = (StyleStruct)Marshal.PtrToStructure(stylePointer, typeof(StyleStruct));
            style.StyleNew |= (int)LayeredStyle;
            Marshal.StructureToPtr(style, stylePointer, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPos
        {
            public IntPtr Handle;
            public IntPtr InsertAfter;
            public int X;
            public int Y;
            public int Cx;
            public int Cy;
            public uint Flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(
            IntPtr handle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr handle, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(
            IntPtr handle,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(
            IntPtr handle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        public static void DisableTransitions(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int disabled = 1;
            DwmSetWindowAttribute(handle, DwmTransitionsForcedDisabled, ref disabled, 4);
        }

        public static void Cloak(IntPtr handle, bool cloak)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int value = cloak ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmCloakAttribute, ref value, 4);
        }

        public static void SuppressUnchangedRedraw(IntPtr handle, IntPtr lParam)
        {
            if (handle == IntPtr.Zero || lParam == IntPtr.Zero)
            {
                return;
            }

            WindowPos position = (WindowPos)Marshal.PtrToStructure(lParam, typeof(WindowPos));
            bool noMove = (position.Flags & SwpNoMove) != 0;
            bool noSize = (position.Flags & SwpNoSize) != 0;
            if (!noMove || !noSize)
            {
                return;
            }

            position.Flags |= SwpNoRedraw;
            Marshal.StructureToPtr(position, lParam, true);
        }

        public static void KeepTopmost(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                handle,
                TopmostInsertAfter,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoRedraw);
        }
    }
}
