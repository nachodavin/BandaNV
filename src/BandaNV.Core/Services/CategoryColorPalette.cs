namespace BandaNV.Core.Services;

public static class CategoryColorPalette
{
    public static string NormalizeOrGenerate(
        string? colorHex,
        string categoryId,
        string secondaryColor)
    {
        if (TryNormalizeHex(colorHex, out var normalized))
        {
            return normalized;
        }

        return Generate(categoryId, secondaryColor);
    }

    public static string Generate(
        string categoryId,
        string secondaryColor)
    {
        var baseHex =
            TryNormalizeHex(secondaryColor, out var normalizedSecondary)
                ? normalizedSecondary
                : "#4FE0C6";

        ParseRgb(baseHex, out var red, out var green, out var blue);
        var (baseHue, _, _) = RgbToHsl(red, green, blue);

        var hash = StableHash(categoryId);
        var hueOffset = (int)(hash % 191) - 95;
        var hue = (baseHue + hueOffset + 360) % 360;
        var saturation = 0.58 + ((hash >> 8) % 13) / 100d;
        var lightness = 0.54 + ((hash >> 16) % 10) / 100d;

        var (r, g, b) = HslToRgb(hue, saturation, lightness);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static bool TryNormalizeHex(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim().TrimStart('#');

        if (hex.Length != 6 ||
            !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        normalized = $"#{hex.ToUpperInvariant()}";
        return true;
    }

    private static void ParseRgb(
        string hex,
        out byte red,
        out byte green,
        out byte blue)
    {
        red = Convert.ToByte(hex.Substring(1, 2), 16);
        green = Convert.ToByte(hex.Substring(3, 2), 16);
        blue = Convert.ToByte(hex.Substring(5, 2), 16);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;

        foreach (var character in value ?? string.Empty)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static (double Hue, double Saturation, double Lightness)
        RgbToHsl(byte red, byte green, byte blue)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = (max + min) / 2d;

        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation =
            delta /
            (1 - Math.Abs(2 * lightness - 1));

        double hue;

        if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, saturation, lightness);
    }

    private static (byte Red, byte Green, byte Blue)
        HslToRgb(
            double hue,
            double saturation,
            double lightness)
    {
        var chroma =
            (1 - Math.Abs(2 * lightness - 1)) *
            saturation;

        var hPrime = hue / 60d;
        var x =
            chroma *
            (1 - Math.Abs(hPrime % 2 - 1));

        var (r1, g1, b1) =
            hPrime switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };

        var m = lightness - chroma / 2d;

        return (
            ToByte(r1 + m),
            ToByte(g1 + m),
            ToByte(b1 + m));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(
            Math.Clamp(value, 0, 1) * 255);
}
