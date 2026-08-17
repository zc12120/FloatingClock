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

        public static void Disable(IntPtr handle)
        {
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
    }
}
