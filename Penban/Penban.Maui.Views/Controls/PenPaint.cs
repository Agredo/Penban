using System.Globalization;
using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Turns the <c>#RRGGBB</c> the app stores colours as into the colour a canvas draws with. The pen
/// colour itself is text all the way down - see <c>PenColor</c> - and only the two picker surfaces
/// need it as a <see cref="Color"/>, so the conversion lives with them rather than in the model.
/// </summary>
internal static class PenPaint
{
    /// <summary>The colour of a stored value, or black if it cannot be read at all.</summary>
    public static Color ToColor(string? hex) =>
        PenColor.TryNormalize(hex, out var normalized)
            ? Color.FromRgb(Channel(normalized, 1), Channel(normalized, 3), Channel(normalized, 5))
            : Colors.Black;

    private static byte Channel(string hex, int start) =>
        byte.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
