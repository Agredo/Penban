namespace Penban.Models;

/// <summary>
/// The colours used last, newest first - the row of swatches at the head of the pen flyout. Kept as
/// the text that was chosen: a colour typed by hand is remembered as it was typed, not as the nearest
/// colour of the palette.
/// </summary>
public static class PenColorHistory
{
    /// <summary>How many colours are kept.</summary>
    public const int Maximum = 5;

    private const char Separator = ';';

    /// <summary>Reads the stored colours, dropping anything unreadable and any repeat.</summary>
    public static IReadOnlyList<string> Parse(string? stored)
    {
        var colors = new List<string>();

        foreach (var part in (stored ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (PenColor.TryNormalize(part, out var hex) && !colors.Contains(hex, StringComparer.OrdinalIgnoreCase))
            {
                colors.Add(hex);
            }
        }

        return colors.Take(Maximum).ToList();
    }

    /// <summary>Writes the colours as one stored value.</summary>
    public static string Format(IReadOnlyList<string> colors) => string.Join(Separator, colors);

    /// <summary>
    /// Puts <paramref name="hex"/> at the head, drops the older entry of the same colour and keeps the
    /// list at <see cref="Maximum"/>: picking the same colour twice moves it up rather than filling the
    /// row with itself. Unreadable text leaves the list as it is.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> colors, string hex)
    {
        if (!PenColor.TryNormalize(hex, out var normalized))
        {
            return colors;
        }

        var updated = new List<string> { normalized };
        updated.AddRange(colors.Where(color => !string.Equals(color, normalized, StringComparison.OrdinalIgnoreCase)));

        return updated.Take(Maximum).ToList();
    }
}
