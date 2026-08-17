using System;
using System.Runtime.InteropServices;

namespace FloatingClock
{
    internal static class DwmGlass
    {
        private const int AccentPolicyAttribute = 19;
        private const int AccentTransparentGradient = 2;
        private const int AccentFlagsFill = 2;
        private const int CornerPreference = 33;
        private const int CornerRoundSmall = 3;
        private const int SystemBackdropType = 38;
        private const int BackdropNone = 1;
        private const int BorderColor = 34;
        private const int CaptionColor = 35;
        private const uint ColorNone = 0xFFFFFFFE;

        public static void Disable(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int square = 1;
            DwmSetWindowAttribute(handle, CornerPreference, ref square, 4);
            int backdrop = BackdropNone;
            DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, 4);
            uint noColor = ColorNone;
            DwmSetWindowAttributeUInt(handle, BorderColor, ref noColor, 4);
            DwmSetWindowAttributeUInt(handle, CaptionColor, ref noColor, 4);
            Apply(handle, 0, 0, false);
        }

        public static void Enable(IntPtr handle, uint accentColor)
        {
            Apply(handle, AccentTransparentGradient, accentColor, true);
        }

        private static void Apply(IntPtr handle, int accentState, uint accentColor, bool extendFrame)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            Margins margins = new Margins
            {
                Left = extendFrame ? -1 : 0,
                Right = extendFrame ? -1 : 0,
                Top = extendFrame ? -1 : 0,
                Bottom = extendFrame ? -1 : 0
            };
            DwmExtendFrameIntoClientArea(handle, ref margins);

            int corner = CornerRoundSmall;
            DwmSetWindowAttribute(handle, CornerPreference, ref corner, 4);
            int backdrop = BackdropNone;
            DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, 4);
            uint noColor = ColorNone;
            DwmSetWindowAttributeUInt(handle, BorderColor, ref noColor, 4);
            DwmSetWindowAttributeUInt(handle, CaptionColor, ref noColor, 4);

            AccentPolicy policy = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = AccentFlagsFill,
                GradientColor = accentColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr policyPointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, policyPointer, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData
                {
                    Attribute = AccentPolicyAttribute,
                    Data = policyPointer,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(policyPointer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr handle,
            ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr handle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int DwmSetWindowAttributeUInt(
            IntPtr handle,
            int attribute,
            ref uint attributeValue,
            int attributeSize);
    }
}
