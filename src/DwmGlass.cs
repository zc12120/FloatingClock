using System;
using System.Runtime.InteropServices;

namespace FloatingClock
{
    internal static class DwmGlass
    {
        private const int AccentPolicyAttribute = 19;
        private const int AccentFlagsFill = 2;
        private const int CornerPreference = 33;
        private const int SystemBackdropType = 38;
        private const int BackdropNone = 1;
        private const int BorderColor = 34;
        private const int CaptionColor = 35;
        private const int TextColor = 36;
        private const uint ColorNone = 0xFFFFFFFE;

        public static void NeutralizeHover(IntPtr handle)
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
            DwmSetWindowAttributeUInt(handle, TextColor, ref noColor, 4);
            ApplyAccent(handle, 0, 0);
        }

        public static void Disable(IntPtr handle)
        {
            NeutralizeHover(handle);
        }

        public static void Enable(IntPtr handle, uint accentColor)
        {
            NeutralizeHover(handle);
        }

        private static void ApplyAccent(IntPtr handle, int accentState, uint accentColor)
        {
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

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr handle,
            ref WindowCompositionAttributeData data);

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
