using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace BandaNV.App.Services;

public static class AppearanceService
{
    private static readonly Windows.UI.Color DefaultPrimary =
        Windows.UI.Color.FromArgb(
            255,
            0x12,
            0x3A,
            0x34);

    private static readonly Windows.UI.Color DefaultSecondary =
        Windows.UI.Color.FromArgb(
            255,
            0x4F,
            0xE0,
            0xC6);

    private static bool _isLightMode;

    public static void ApplySettings(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        var primary =
            TryParseHexColor(
                settings.PrimaryColor,
                out var parsedPrimary)
                ? parsedPrimary
                : DefaultPrimary;

        var secondary =
            TryParseHexColor(
                settings.SecondaryColor,
                out var parsedSecondary)
                ? parsedSecondary
                : DefaultSecondary;

        ApplyTheme(
            settings.Theme,
            primary,
            secondary);
    }

    public static void ApplyTheme(
        string? theme,
        Windows.UI.Color primary,
        Windows.UI.Color secondary)
    {
        _isLightMode =
            ResolveLightMode(
                theme);

        ApplyNeutralThemeResources();
        ApplyPrimaryColor(
            primary);
        ApplySecondaryColor(
            secondary);
    }

    public static ElementTheme GetElementTheme(
        string? theme) =>
        theme?.Trim() switch
        {
            "Claro" => ElementTheme.Light,
            "Oscuro" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public static bool ResolveLightMode(
        string? theme) =>
        theme?.Trim() switch
        {
            "Claro" => true,
            "Oscuro" => false,
            _ => IsWindowsLightTheme()
        };

    public static void ApplyPrimaryColor(
        Windows.UI.Color color)
    {
        SetSolidBrushColor(
            "BandaPrimaryColorBrush",
            color);

        var backgroundBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xF4,
                    0xF7,
                    0xF8)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x06,
                    0x0A,
                    0x0D);

        var sidebarBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xEE,
                    0xF2,
                    0xF3)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x07,
                    0x0C,
                    0x10);

        var cardBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xFF,
                    0xFF,
                    0xFF)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x0C,
                    0x12,
                    0x16);

        var cardAltBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xF2,
                    0xF5,
                    0xF6)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x0D,
                    0x14,
                    0x18);

        var popupBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0xFF,
                    0xFF,
                    0xFF)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x08,
                    0x0E,
                    0x12);

        var borderBase =
            _isLightMode
                ? Windows.UI.Color.FromArgb(
                    255,
                    0x98,
                    0xA6,
                    0xAD)
                : Windows.UI.Color.FromArgb(
                    255,
                    0x43,
                    0x50,
                    0x59);

        var background =
            BlendColor(
                backgroundBase,
                color,
                _isLightMode
                    ? 0.035
                    : 0.07);

        var sidebar =
            BlendColor(
                sidebarBase,
                color,
                _isLightMode
                    ? 0.045
                    : 0.09);

        var card =
            BlendColor(
                cardBase,
                color,
                _isLightMode
                    ? 0.035
                    : 0.10);

        var cardAlt =
            BlendColor(
                cardAltBase,
                color,
                _isLightMode
                    ? 0.055
                    : 0.14);

        var navIcon =
            BlendColor(
                cardAltBase,
                color,
                _isLightMode
                    ? 0.075
                    : 0.16);

        var popup =
            BlendColor(
                popupBase,
                color,
                _isLightMode
                    ? 0.03
                    : 0.10);

        var border =
            BlendColor(
                borderBase,
                color,
                _isLightMode
                    ? 0.07
                    : 0.10);

        var borderStrong =
            BlendColor(
                borderBase,
                color,
                _isLightMode
                    ? 0.10
                    : 0.16);

        var accentCard =
            BlendColor(
                cardBase,
                color,
                _isLightMode
                    ? 0.12
                    : 0.22);

        SetSolidBrushColor(
            "BandaBackgroundBrush",
            background);

        SetSolidBrushColor(
            "BandaAppBackgroundGradient",
            background);

        SetSolidBrushColor(
            "BandaSidebarBrush",
            sidebar);

        SetSolidBrushColor(
            "BandaSidebarGradientBrush",
            sidebar);

        SetSolidBrushColor(
            "BandaCardBrush",
            card);

        SetSolidBrushColor(
            "BandaCardAltBrush",
            cardAlt);

        SetSolidBrushColor(
            "BandaSurfaceBrush",
            card);

        SetSolidBrushColor(
            "BandaSurfaceAltBrush",
            cardAlt);

        SetSolidBrushColor(
            "BandaNavIconBrush",
            navIcon);

        SetSolidBrushColor(
            "BandaPopupSurfaceBrush",
            popup);

        SetSolidBrushColor(
            "BandaBorderBrush",
            WithAlpha(
                border,
                _isLightMode
                    ? (byte)0x78
                    : (byte)0x4A));

        SetSolidBrushColor(
            "BandaBorderStrongBrush",
            WithAlpha(
                borderStrong,
                _isLightMode
                    ? (byte)0xA0
                    : (byte)0x72));

        SetSolidBrushColor(
            "BandaAccentCardBrush",
            accentCard);
    }

    public static void ApplySecondaryColor(
        Windows.UI.Color color)
    {
        SetSolidBrushColor(
            "BandaSecondaryColorBrush",
            color);

        SetSolidBrushColor(
            "BandaAccentBrush",
            color);

        SetSolidBrushColor(
            "BandaAccentSoftBrush",
            WithAlpha(
                color,
                _isLightMode
                    ? (byte)0x30
                    : (byte)0x24));

        SetSolidBrushColor(
            "BandaAccentFaintBrush",
            WithAlpha(
                color,
                _isLightMode
                    ? (byte)0x18
                    : (byte)0x12));

        SetSolidBrushColor(
            "BandaNavActiveBrush",
            WithAlpha(
                color,
                _isLightMode
                    ? (byte)0x2C
                    : (byte)0x22));

        SetSolidBrushColor(
            "BandaAccentGradientBrush",
            color);

        var hover =
            BlendColor(
                color,
                Windows.UI.Color.FromArgb(
                    255,
                    255,
                    255,
                    255),
                0.14);

        var pressed =
            ScaleColor(
                color,
                0.80);

        var secondaryHighlight =
            BlendColor(
                color,
                Windows.UI.Color.FromArgb(
                    255,
                    255,
                    255,
                    255),
                0.18);

        var foreground =
            GetReadableForeground(
                color);

        SetSolidBrushColor(
            "BandaSecondaryAccentBrush",
            secondaryHighlight);

        SetSolidBrushColor(
            "BandaActionHoverBrush",
            hover);

        SetSolidBrushColor(
            "BandaActionPressedBrush",
            pressed);

        SetSolidBrushColor(
            "BandaActionForegroundBrush",
            foreground);

        SetColorResource(
            "BandaActionBaseColor",
            color);

        SetColorResource(
            "BandaActionHoverColor",
            hover);

        SetColorResource(
            "BandaActionPressedColor",
            pressed);

        SetColorResource(
            "BandaActionForegroundColor",
            foreground);

        SetColorResource(
            "BandaSelectionColor",
            WithAlpha(
                color,
                0x24));

        SetColorResource(
            "BandaSelectionHoverColor",
            WithAlpha(
                color,
                0x30));

        SetSolidBrushColor(
            "ToggleSwitchFillOn",
            color);

        SetSolidBrushColor(
            "ToggleSwitchFillOnPointerOver",
            hover);

        SetSolidBrushColor(
            "ToggleSwitchFillOnPressed",
            pressed);

        SetSolidBrushColor(
            "ToggleSwitchStrokeOn",
            color);

        SetSolidBrushColor(
            "ToggleSwitchStrokeOnPointerOver",
            hover);

        SetSolidBrushColor(
            "ToggleSwitchStrokeOnPressed",
            pressed);

        SetSolidBrushColor(
            "ToggleSwitchKnobFillOn",
            foreground);

        SetSolidBrushColor(
            "ToggleSwitchKnobFillOnPointerOver",
            foreground);

        SetSolidBrushColor(
            "ToggleSwitchKnobFillOnPressed",
            foreground);
    }

    public static void UpdateActionButtonResources(
        Button button,
        Windows.UI.Color color)
    {
        ArgumentNullException.ThrowIfNull(
            button);

        var hover =
            BlendColor(
                color,
                Windows.UI.Color.FromArgb(
                    255,
                    255,
                    255,
                    255),
                0.14);

        var pressed =
            ScaleColor(
                color,
                0.80);

        var foreground =
            GetReadableForeground(
                color);

        UpdateLocalActionButtonResources(
            button,
            color,
            hover,
            pressed,
            foreground);
    }

    private static void ApplyNeutralThemeResources()
    {
        if (_isLightMode)
        {
            SetSolidBrushColor(
                "BandaTextBrush",
                Windows.UI.Color.FromArgb(
                    255,
                    0x17,
                    0x22,
                    0x28));

            SetSolidBrushColor(
                "BandaMutedBrush",
                Windows.UI.Color.FromArgb(
                    255,
                    0x68,
                    0x75,
                    0x7C));

            SetSolidBrushColor(
                "BandaMutedStrongBrush",
                Windows.UI.Color.FromArgb(
                    255,
                    0x45,
                    0x53,
                    0x5B));

            SetSolidBrushColor(
                "BandaActionDisabledBackgroundBrush",
                Windows.UI.Color.FromArgb(
                    0x18,
                    0x4A,
                    0x56,
                    0x5E));

            SetSolidBrushColor(
                "BandaActionDisabledForegroundBrush",
                Windows.UI.Color.FromArgb(
                    255,
                    0x8A,
                    0x96,
                    0x9D));

            SetSolidBrushColor(
                "BandaActionDisabledBorderBrush",
                Windows.UI.Color.FromArgb(
                    0x60,
                    0x8A,
                    0x96,
                    0x9D));

            SetSolidBrushColor(
                "BandaPopupBackdropBrush",
                Windows.UI.Color.FromArgb(
                    0x66,
                    0x08,
                    0x0C,
                    0x11));

            SetSolidBrushColor(
                "BandaDangerBrush",
                Windows.UI.Color.FromArgb(
                    255,
                    0xD8,
                    0x52,
                    0x52));

            SetSolidBrushColor(
                "BandaDangerSoftBrush",
                Windows.UI.Color.FromArgb(
                    0x24,
                    0xD8,
                    0x52,
                    0x52));

            return;
        }

        SetSolidBrushColor(
            "BandaTextBrush",
            Windows.UI.Color.FromArgb(
                255,
                0xF5,
                0xF8,
                0xFA));

        SetSolidBrushColor(
            "BandaMutedBrush",
            Windows.UI.Color.FromArgb(
                255,
                0x93,
                0xA1,
                0xAA));

        SetSolidBrushColor(
            "BandaMutedStrongBrush",
            Windows.UI.Color.FromArgb(
                255,
                0xC1,
                0xCB,
                0xD1));

        SetSolidBrushColor(
            "BandaActionDisabledBackgroundBrush",
            Windows.UI.Color.FromArgb(
                0x15,
                0x1D,
                0x26,
                0x2F));

        SetSolidBrushColor(
            "BandaActionDisabledForegroundBrush",
            Windows.UI.Color.FromArgb(
                255,
                0x65,
                0x74,
                0x7D));

        SetSolidBrushColor(
            "BandaActionDisabledBorderBrush",
            Windows.UI.Color.FromArgb(
                0x35,
                0x42,
                0x4E,
                0x59));

        SetSolidBrushColor(
            "BandaPopupBackdropBrush",
            Windows.UI.Color.FromArgb(
                0xB3,
                0x08,
                0x0C,
                0x11));

        SetSolidBrushColor(
            "BandaDangerBrush",
            Windows.UI.Color.FromArgb(
                255,
                0xFF,
                0x7B,
                0x7B));

        SetSolidBrushColor(
            "BandaDangerSoftBrush",
            Windows.UI.Color.FromArgb(
                0x24,
                0xFF,
                0x7B,
                0x7B));
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            var value =
                key?.GetValue(
                    "AppsUseLightTheme");

            return value is int intValue &&
                   intValue != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Windows.UI.Color GetReadableForeground(
        Windows.UI.Color background)
    {
        var luminance =
            (0.2126 * background.R +
             0.7152 * background.G +
             0.0722 * background.B) /
            255.0;

        return luminance >= 0.56
            ? Windows.UI.Color.FromArgb(
                255,
                0x07,
                0x11,
                0x0F)
            : Windows.UI.Color.FromArgb(
                255,
                0xF5,
                0xF8,
                0xFA);
    }

    private static void UpdateLocalActionButtonResources(
        Button button,
        Windows.UI.Color normal,
        Windows.UI.Color hover,
        Windows.UI.Color pressed,
        Windows.UI.Color foreground)
    {
        if (button.Resources["ButtonBackground"] is
            SolidColorBrush background)
        {
            background.Color =
                normal;
        }

        if (button.Resources["ButtonBackgroundPointerOver"] is
            SolidColorBrush backgroundHover)
        {
            backgroundHover.Color =
                hover;
        }

        if (button.Resources["ButtonBackgroundPressed"] is
            SolidColorBrush backgroundPressed)
        {
            backgroundPressed.Color =
                pressed;
        }

        if (button.Resources["ButtonBorderBrush"] is
            SolidColorBrush border)
        {
            border.Color =
                normal;
        }

        if (button.Resources["ButtonBorderBrushPointerOver"] is
            SolidColorBrush borderHover)
        {
            borderHover.Color =
                hover;
        }

        if (button.Resources["ButtonBorderBrushPressed"] is
            SolidColorBrush borderPressed)
        {
            borderPressed.Color =
                pressed;
        }

        foreach (var key in new[]
                 {
                     "ButtonForeground",
                     "ButtonForegroundPointerOver",
                     "ButtonForegroundPressed"
                 })
        {
            if (button.Resources[key] is
                SolidColorBrush brush)
            {
                brush.Color =
                    foreground;
            }
        }
    }

    private static void SetSolidBrushColor(
        string resourceKey,
        Windows.UI.Color color)
    {
        if (Application.Current.Resources[resourceKey] is
            SolidColorBrush brush)
        {
            brush.Color =
                color;
        }
    }

    private static void SetColorResource(
        string resourceKey,
        Windows.UI.Color color)
    {
        Application.Current.Resources[resourceKey] =
            color;
    }

    private static Windows.UI.Color WithAlpha(
        Windows.UI.Color color,
        byte alpha) =>
        Windows.UI.Color.FromArgb(
            alpha,
            color.R,
            color.G,
            color.B);

    private static Windows.UI.Color ScaleColor(
        Windows.UI.Color color,
        double factor) =>
        Windows.UI.Color.FromArgb(
            color.A,
            (byte)Math.Clamp(
                (int)Math.Round(
                    color.R *
                    factor),
                0,
                255),
            (byte)Math.Clamp(
                (int)Math.Round(
                    color.G *
                    factor),
                0,
                255),
            (byte)Math.Clamp(
                (int)Math.Round(
                    color.B *
                    factor),
                0,
                255));

    private static Windows.UI.Color BlendColor(
        Windows.UI.Color baseColor,
        Windows.UI.Color accent,
        double accentWeight)
    {
        var baseWeight =
            1.0 -
            accentWeight;

        return Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Clamp(
                (int)Math.Round(
                    baseColor.R *
                    baseWeight +
                    accent.R *
                    accentWeight),
                0,
                255),
            (byte)Math.Clamp(
                (int)Math.Round(
                    baseColor.G *
                    baseWeight +
                    accent.G *
                    accentWeight),
                0,
                255),
            (byte)Math.Clamp(
                (int)Math.Round(
                    baseColor.B *
                    baseWeight +
                    accent.B *
                    accentWeight),
                0,
                255));
    }

    private static bool TryParseHexColor(
        string? value,
        out Windows.UI.Color color)
    {
        color =
            DefaultSecondary;

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        var hex =
            value
                .Trim()
                .TrimStart('#');

        if (hex.Length != 6 ||
            !byte.TryParse(
                hex[..2],
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var red) ||
            !byte.TryParse(
                hex.Substring(
                    2,
                    2),
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var green) ||
            !byte.TryParse(
                hex.Substring(
                    4,
                    2),
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var blue))
        {
            return false;
        }

        color =
            Windows.UI.Color.FromArgb(
                255,
                red,
                green,
                blue);

        return true;
    }
}
