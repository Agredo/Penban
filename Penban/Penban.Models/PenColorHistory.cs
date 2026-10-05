namespace Penban.Models;

/// <summary>
/// The colours used last, newest first - the row of swatches at the head of the pen flyout. Kept as
/// the text that was chosen: a colour typed by hand is remembered as it was typed, not as the nearest
/// colour of the palette.
/// <para>
/// Used is meant as written with, not looked at: a colour reaches this row once a stroke has been drawn
/// in it, so mixing a shade with the picker only to settle on another one leaves the row alone.
/// </para>
/// </summary>
public static class PenColorHistory
{
    /// <summary>How many colours are kept.</summary>
    public const int Maximum = 5;

    private const char Separator = ';';

    /// <summary>
    /// Reads the stored colours, dropping anything unreadable and any repeat - and a colour that is not
    /// told apart from one already read counts as a repeat, so a row that was filled by dragging along
    /// the picker comes back as the colours it was meant to hold.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? stored)
    {
        var colors = new List<string>();

        foreach (var part in (stored ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (PenColor.TryNormalize(part, out var hex) && !colors.Any(known => PenColor.AreNear(known, hex)))
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
    /// row with itself. A colour that is not told apart from the one being added goes with it - the row
    /// is a row of colours that can be told apart, and a shade that differs from one already in it by
    /// less than the eye reads is the same swatch twice. Unreadable text leaves the list as it is.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> colors, string hex)
    {
        if (!PenColor.TryNormalize(hex, out var normalized))
        {
            return colors;
        }

        var updated = new List<string> { normalized };
        updated.AddRange(colors.Where(color => !PenColor.AreNear(color, normalized)));

        return updated.Take(Maximum).ToList();
    }
}
