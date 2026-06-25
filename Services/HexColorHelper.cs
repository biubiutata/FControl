namespace FControl.Services;

public static class HexColorHelper
{
    public static bool TryNormalizeRgb(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < trimmed.Length; i++)
        {
            if (!Uri.IsHexDigit(trimmed[i]))
            {
                return false;
            }
        }

        normalized = trimmed.ToUpperInvariant();
        return true;
    }

    public static string NormalizeRgb(string? value, string fallback)
    {
        return TryNormalizeRgb(value, out var normalized) ? normalized : fallback;
    }

    public static bool TryParseRgb(string? value, out byte red, out byte green, out byte blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        if (!TryNormalizeRgb(value, out var normalized))
        {
            return false;
        }

        red = Convert.ToByte(normalized.Substring(1, 2), 16);
        green = Convert.ToByte(normalized.Substring(3, 2), 16);
        blue = Convert.ToByte(normalized.Substring(5, 2), 16);
        return true;
    }
}
