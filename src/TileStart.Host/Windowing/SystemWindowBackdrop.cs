using System.Runtime.InteropServices;

namespace TileStart.Host.Windowing;

internal static class SystemWindowBackdrop
{
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int BackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropTransientWindow = 3;
    private const int ColorNone = unchecked((int)0xFFFFFFFE);

    public static bool TryApplyDesktopAcrylic(nint window, bool useDarkMode)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            return false;
        }

        _ = SetAttribute(window, UseImmersiveDarkMode, useDarkMode ? 1 : 0);
        _ = SetAttribute(window, CaptionColor, ColorNone);
        if (SetAttribute(window, BackdropType, BackdropTransientWindow) != 0)
        {
            return false;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(window, ref margins) == 0)
        {
            return true;
        }

        Clear(window);
        return false;
    }

    public static void Clear(nint window)
    {
        _ = SetAttribute(window, BackdropType, BackdropNone);
        var margins = new Margins();
        _ = DwmExtendFrameIntoClientArea(window, ref margins);
    }

    private static int SetAttribute(nint window, int attribute, int value) =>
        DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
