using System.Globalization;

namespace Penban.Models;

/// <summary>
/// A pen colour as the two things the app needs from one: the <c>#RRGGBB</c> text it is stored and
/// written in, and the hue/saturation/value the colour picker works in. Both directions are here, so
/// a colour typed by hand and a colour picked off the square are the same colour.
/// </summary>
public static class PenColor
{
    /// <summary>Digits of the short form, <c>#RGB</c>.</summary>
    private const int ShortDigits = 3;

    /// <summary>Digits of the long form, <c>#RRGGBB</c>, which is what is stored.</summary>
    private const int LongDigits = 6;

    /// <summary>
    /// Reads a colour written by hand: <c>#RGB</c> or <c>#RRGGBB</c>, with or without the <c>#</c>, in
    /// either case, and hands it back as <c>#RRGGBB</c> in capitals. False means the text is not a
    /// colour at all, which is what the field beside it says rather than guessing one.
    /// </summary>
    public static bool TryNormalize(string? text, out string hex)
    {
        hex = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = text.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        if (digits.Length == ShortDigits)
        {
            // #1B4 is #11BB44: every digit stands in for itself twice over.
            digits = string.Concat(digits.Select(digit => new string(digit, 2)));
        }

        if (digits.Length != LongDigits || !digits.All(Uri.IsHexDigit))
        {
            return false;
        }

        hex = string.Concat("#", digits.ToUpperInvariant());
        return true;
    }

    /// <summary>
    /// Hue in degrees (0-360), saturation and value (0-1) of a colour written as <c>#RRGGBB</c>.
    /// Unreadable text comes back as black, which is the colour a pen starts with.
    /// </summary>
    public static (float Hue, float Saturation, float Value) ToHsv(string hex)
    {
        var (red, green, blue) = ToComponents(hex);
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var span = maximum - minimum;

        var hue = 0f;
        if (span > 0f)
        {
            hue = maximum == red
                ? 60f * (((green - blue) / span) % 6f)
                : maximum == green
                    ? 60f * (((blue - red) / span) + 2f)
                    : 60f * (((red - green) / span) + 4f);
        }

        if (hue < 0f)
        {
            hue += 360f;
        }

        var saturation = maximum <= 0f ? 0f : span / maximum;
        return (hue, saturation, maximum);
    }

    /// <summary>
    /// The <c>#RRGGBB</c> of a hue in degrees (0-360) and a saturation and value of 0-1 - the way back
    /// from the picker to a colour. Both ends of the range are included, so the square's four corners
    /// are white, black and the two full-strength colours.
    /// </summary>
    public static string FromHsv(float hue, float saturation, float value)
    {
        var normalizedHue = ((hue % 360f) + 360f) % 360f;
        var bright = Math.Clamp(value, 0f, 1f);
        var chroma = bright * Math.Clamp(saturation, 0f, 1f);
        var second = chroma * (1f - Math.Abs(((normalizedHue / 60f) % 2f) - 1f));
        var match = bright - chroma;

        var (red, green, blue) = normalizedHue switch
        {
            < 60f => (chroma, second, 0f),
            < 120f => (second, chroma, 0f),
            < 180f => (0f, chroma, second),
            < 240f => (0f, second, chroma),
            < 300f => (second, 0f, chroma),
            _ => (chroma, 0f, second),
        };

        return ToHex(red + match, green + match, blue + match);
    }

    /// <summary>Red, green and blue of a colour written as <c>#RRGGBB</c>, each 0-1.</summary>
    private static (float Red, float Green, float Blue) ToComponents(string hex)
    {
        if (!TryNormalize(hex, out var normalized))
        {
            return (0f, 0f, 0f);
        }

        return (
            ChannelOf(normalized.AsSpan(1, 2)) / 255f,
            ChannelOf(normalized.AsSpan(3, 2)) / 255f,
            ChannelOf(normalized.AsSpan(5, 2)) / 255f);
    }

    private static byte ChannelOf(ReadOnlySpan<char> digits) =>
        byte.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string ToHex(float red, float green, float blue) =>
        $"#{Channel(red):X2}{Channel(green):X2}{Channel(blue):X2}";

    private static byte Channel(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
