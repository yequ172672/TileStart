using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using TileStart.Host.Themes;
using MediaColor = System.Windows.Media.Color;

namespace TileStart.Host;

internal readonly record struct StartMaterialConfiguration(
    bool UseAcrylic,
    MediaColor FallbackColor,
    int AcrylicGradientColor,
    MediaColor LiveResizeColor);

public static class Win10Theme
{
    private const string AccentRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int AccentPaletteOffset = 3 * 4;

    private const int StartAcrylicGradientColor = unchecked((int)0xBF101010);

    // StartUI LightThemeEnabledNormalAcrylic: #E4E4E4 at TintOpacity 0.853.
    // WCA does not expose TintOpacity separately, so 0xDA is the compatible alpha mapping.
    private const int StartLightAcrylicGradientColor = unchecked((int)0xDAE4E4E4);

    // 原版 UWP TintOpacity=0.8 不能直接等同于 WCA GradientColor alpha；0xCC 会把
    // 壁纸压成均匀的高饱和蓝板。0xB8 是同壁纸对照 Win10 原生截图后的兼容校准值。
    private const uint Win10AccentAcrylicTintAlpha = 0xB8;

    // WCA 缺少 UWP Acrylic 的 luminosity 合成。修正 Dark1 来源后，同壁纸 ROI 表明 16% 会明显压暗；
    // 7% 是当前跨原生/复刻截图的兼容拟合值，不是 StartUI 内部公开参数。
    private const double Win10AccentAcrylicNeutralBlend = 0.07;

    // Win10 19045 同一强调色样本中，Dark2 相当于先降低约 15% value，再把 HSV saturation
    // 放大约 1.26 倍；该兼容派生用于 NavigationPane 的独立 0.5 tint，不能读取宿主 Win11 Palette[5]。
    private const double Win10Dark2DarkenAmount = 0.15;
    private const double Win10Dark2SaturationScale = 1.26;
    private const byte Win10NavigationOverlayAlpha = 0x80;

    private static readonly MediaColor StartFallbackColor = MediaColor.FromRgb(0x1F, 0x1F, 0x1F);
    private static readonly MediaColor StartLightFallbackColor = MediaColor.FromRgb(0xE4, 0xE4, 0xE4);

    public static MediaColor AccentColor { get; } = ReadAccentColor();

    public static SolidColorBrush AccentBrush { get; } = CreateFrozenBrush(AccentColor);

    public static SolidColorBrush AccentHoverBrush { get; } = CreateFrozenBrush(Blend(AccentColor, Colors.White, 0.10));

    public static SolidColorBrush AccentPressedBrush { get; } =
        CreateFrozenBrush(Blend(AccentColor, Colors.Black, 0.12));

    public static SolidColorBrush AccentForegroundBrush { get; } =
        CreateFrozenBrush(UseDarkForeground(AccentColor) ? Colors.Black : Colors.White);

    public static SolidColorBrush ContextMenuHighlightBrush => AccentBrush;

    public static MediaColor NavigationOverlayColor { get; } = ReadNavigationOverlayColor();

    internal static MediaColor ResolveAccentColor(object? accentColorMenu, byte[]? palette, MediaColor fallback)
    {
        if (TryReadAccentColorMenu(accentColorMenu, out var color))
        {
            return color;
        }

        return palette is { Length: >= AccentPaletteOffset + 3 }
            ? MediaColor.FromRgb(palette[AccentPaletteOffset], palette[AccentPaletteOffset + 1],
                palette[AccentPaletteOffset + 2])
            : fallback;
    }

    internal static StartMaterialConfiguration ResolveStartMaterial(object? enableTransparency, bool highContrast)
        => ResolveStartMaterial(enableTransparency, highContrast, AppThemeStyle.Windows10);

    internal static StartMaterialConfiguration ResolveStartMaterial(
        object? enableTransparency,
        bool highContrast,
        AppThemeStyle themeStyle,
        bool useDarkMode = true)
        => ResolveStartMaterial(
            enableTransparency,
            highContrast,
            themeStyle,
            startColorMenu: null,
            useDarkMode: useDarkMode);

    internal static StartMaterialConfiguration ResolveStartMaterial(
        object? enableTransparency,
        bool highContrast,
        AppThemeStyle themeStyle,
        object? startColorMenu,
        bool useDarkMode = true)
        => ResolveStartMaterial(
            enableTransparency,
            highContrast,
            themeStyle,
            startColorMenu,
            colorPrevalence: null,
            accentPalette: null,
            useDarkMode: useDarkMode);

    internal static StartMaterialConfiguration ResolveStartMaterial(
        object? enableTransparency,
        bool highContrast,
        AppThemeStyle themeStyle,
        object? startColorMenu,
        object? colorPrevalence,
        byte[]? accentPalette,
        object? accentColorMenu = null,
        bool useDarkMode = true)
    {
        var useAcrylic = !highContrast && enableTransparency is int value && value != 0;
        if (themeStyle == AppThemeStyle.Windows11)
        {
            var neutral = useDarkMode
                ? MediaColor.FromRgb(0x20, 0x20, 0x20)
                : MediaColor.FromRgb(0xF3, 0xF3, 0xF3);
            return new StartMaterialConfiguration(useAcrylic, neutral, 0, neutral);
        }

        var useWin10AccentAcrylic = useDarkMode && colorPrevalence is int prevalence && prevalence != 0;
        var win10AccentColor = useWin10AccentAcrylic
            ? ResolveWin10AccentAcrylicColor(accentColorMenu, startColorMenu, accentPalette)
            : StartFallbackColor;
        var useWin10LightAcrylic = !useDarkMode;
        var fallbackColor = useWin10AccentAcrylic && !highContrast
            ? win10AccentColor
            : useWin10LightAcrylic
                ? StartLightFallbackColor
                : StartFallbackColor;
        return new StartMaterialConfiguration(
            useAcrylic,
            fallbackColor,
            useWin10AccentAcrylic
                ? PackAccentPolicyColor(
                    ResolveWin10AccentWcaTintColor(win10AccentColor),
                    Win10AccentAcrylicTintAlpha)
                : useWin10LightAcrylic
                    ? StartLightAcrylicGradientColor
                    : StartAcrylicGradientColor,
            fallbackColor);
    }

    internal static MediaColor ResolveWin10AccentWcaTintColor(MediaColor accentDark1) =>
        Blend(accentDark1, StartFallbackColor, Win10AccentAcrylicNeutralBlend);

    /// <summary>
    /// Win10 在 ColorPrevalence 开启时直接发布 StartColorMenu，优先使用该最终 Start 色。
    /// Win11 宿主或损坏配置可能缺少/滞留此值，此时才从 AccentColorMenu/Palette[3] 派生 Dark1。
    /// </summary>
    internal static MediaColor ResolveWin10AccentAcrylicColor(
        object? accentColorMenu,
        object? startColorMenu,
        byte[]? accentPalette)
    {
        if (TryReadPackedColor(startColorMenu, out var packed))
        {
            return MediaColor.FromRgb((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16));
        }

        if (TryReadAccentColorMenu(accentColorMenu, out var accent))
        {
            return DeriveWin10Dark1(accent);
        }

        if (accentPalette is { Length: >= AccentPaletteOffset + 3 })
        {
            return DeriveWin10Dark1(MediaColor.FromRgb(
                accentPalette[AccentPaletteOffset],
                accentPalette[AccentPaletteOffset + 1],
                accentPalette[AccentPaletteOffset + 2]));
        }

        return StartFallbackColor;
    }

    internal static MediaColor DeriveWin10Dark1(MediaColor accentColor) =>
        Blend(accentColor, Colors.Black, 0.10);

    internal static MediaColor DeriveWin10Dark2(MediaColor accentColor) =>
        ScaleSaturation(
            Blend(accentColor, Colors.Black, Win10Dark2DarkenAmount),
            Win10Dark2SaturationScale);

    internal static MediaColor ResolveWin10NavigationOverlayColor(
        object? colorPrevalence,
        object? accentColorMenu,
        byte[]? accentPalette,
        MediaColor neutralOverlay)
    {
        if (colorPrevalence is not int prevalence || prevalence == 0)
        {
            return neutralOverlay;
        }

        MediaColor accent;
        if (!TryReadAccentColorMenu(accentColorMenu, out accent))
        {
            if (accentPalette is not { Length: >= AccentPaletteOffset + 3 })
            {
                return neutralOverlay;
            }

            accent = MediaColor.FromRgb(
                accentPalette[AccentPaletteOffset],
                accentPalette[AccentPaletteOffset + 1],
                accentPalette[AccentPaletteOffset + 2]);
        }

        var dark2 = DeriveWin10Dark2(accent);
        return MediaColor.FromArgb(Win10NavigationOverlayAlpha, dark2.R, dark2.G, dark2.B);
    }

    internal static StartMaterialConfiguration ReadStartMaterial(AppThemeStyle themeStyle, bool useDarkMode = true)
    {
        object? enableTransparency = null;
        object? colorPrevalence = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            enableTransparency = key?.GetValue("EnableTransparency");
            colorPrevalence = key?.GetValue("ColorPrevalence");
        }
        catch (Exception)
        {
        }

        object? startColorMenu = null;
        object? accentColorMenu = null;
        byte[]? accentPalette = null;
        try
        {
            if (themeStyle == AppThemeStyle.Windows10)
            {
                using var key = Registry.CurrentUser.OpenSubKey(AccentRegistryPath);
                startColorMenu = key?.GetValue("StartColorMenu");
                accentColorMenu = key?.GetValue("AccentColorMenu");
                accentPalette = key?.GetValue("AccentPalette") as byte[];
            }
        }
        catch (Exception)
        {
        }

        return ResolveStartMaterial(
            enableTransparency,
            SystemParameters.HighContrast,
            themeStyle,
            startColorMenu,
            colorPrevalence,
            accentPalette,
            accentColorMenu,
            useDarkMode);
    }

    private static MediaColor ReadNavigationOverlayColor()
    {
        object? colorPrevalence = null;
        object? accentColorMenu = null;
        byte[]? accentPalette = null;
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            colorPrevalence = personalize?.GetValue("ColorPrevalence");
            using var accent = Registry.CurrentUser.OpenSubKey(AccentRegistryPath);
            accentColorMenu = accent?.GetValue("AccentColorMenu");
            accentPalette = accent?.GetValue("AccentPalette") as byte[];
        }
        catch (Exception)
        {
        }

        return ResolveWin10NavigationOverlayColor(
            colorPrevalence,
            accentColorMenu,
            accentPalette,
            MediaColor.FromArgb(Win10NavigationOverlayAlpha, 0x2C, 0x2C, 0x2C));
    }

    private static MediaColor ReadAccentColor()
    {
        var fallback = SystemParameters.WindowGlassColor;
        object? accentColorMenu = null;
        byte[]? palette = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentRegistryPath);
            accentColorMenu = key?.GetValue("AccentColorMenu");
            palette = key?.GetValue("AccentPalette") as byte[];
        }
        catch (Exception)
        {
        }

        return ResolveAccentColor(accentColorMenu, palette, fallback);
    }

    private static bool TryReadAccentColorMenu(object? value, out MediaColor color)
    {
        if (!TryReadPackedColor(value, out var packed))
        {
            color = default;
            return false;
        }

        color = MediaColor.FromRgb(
            (byte)packed,
            (byte)(packed >> 8),
            (byte)(packed >> 16));
        return true;
    }

    private static bool TryReadPackedColor(object? value, out uint packed)
    {
        switch (value)
        {
            case int signed:
                packed = unchecked((uint)signed);
                return true;
            case uint unsigned:
                packed = unsigned;
                return true;
            default:
                packed = 0;
                return false;
        }
    }

    private static int PackAccentPolicyColor(MediaColor color, uint alpha) =>
        unchecked((int)((alpha << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R));

    internal static MediaColor Blend(MediaColor source, MediaColor target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return MediaColor.FromRgb(
            (byte)Math.Round(source.R + ((target.R - source.R) * amount)),
            (byte)Math.Round(source.G + ((target.G - source.G) * amount)),
            (byte)Math.Round(source.B + ((target.B - source.B) * amount)));
    }

    internal static MediaColor ScaleSaturation(MediaColor color, double factor)
    {
        factor = Math.Max(0, factor);
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));

        static byte ScaleChannel(byte channel, byte maximum, double factor) =>
            (byte)Math.Clamp(Math.Round(maximum - ((maximum - channel) * factor)), 0, 255);

        return MediaColor.FromArgb(
            color.A,
            ScaleChannel(color.R, maximum, factor),
            ScaleChannel(color.G, maximum, factor),
            ScaleChannel(color.B, maximum, factor));
    }

    internal static bool UseDarkForeground(MediaColor background)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linearize(background.R))
                        + (0.7152 * Linearize(background.G))
                        + (0.0722 * Linearize(background.B));
        return luminance > 0.44;
    }

    private static SolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
