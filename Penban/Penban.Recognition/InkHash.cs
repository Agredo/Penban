using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// A content hash of a card's ink, used as the "has anything changed" half of the recognition cache
/// key.
/// <para>
/// It hashes what recognition actually depends on - the stroke geometry and the stroke width - and
/// deliberately ignores colour, pressure, tilt and timestamps. Those do not reach the recognizer, so
/// including them would re-run recognition every time the user recoloured a card or an import
/// re-stamped its points, for text that cannot possibly differ.
/// </para>
/// <para>
/// Coordinates are rounded before hashing because the rasteriser cannot resolve finer than a
/// thousandth of a document unit, and an unrounded hash would treat imperceptible float noise as a
/// change.
/// </para>
/// </summary>
public static class InkHash
{
    /// <summary>Decimal places kept per coordinate. A document unit is one pixel of a 1000x1000 space.</summary>
    private const int CoordinatePrecision = 3;

    /// <summary>
    /// Returns the lower-case hex SHA-256 of the canonical form of <paramref name="strokes"/>. Two
    /// stroke lists produce the same hash exactly when they would render to the same line images.
    /// </summary>
    public static string Compute(IReadOnlyList<InkStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        var canonical = new StringBuilder(strokes.Count * 64);
        foreach (var stroke in strokes)
        {
            canonical.Append(Format(stroke.Thickness)).Append(':').Append(stroke.Points.Count).Append(';');

            foreach (var point in stroke.Points)
            {
                canonical.Append(Format(point.X)).Append(',');
                canonical.Append(Format(point.Y)).Append(';');
            }

            canonical.Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Round-trip-safe, culture-independent number formatting. <c>InvariantCulture</c> is required:
    /// under a German locale the default format writes <c>1,5</c>, which would change the bytes of the
    /// hash for the same ink depending on the user's language.
    /// </summary>
    private static string Format(float value) =>
        MathF.Round(value, CoordinatePrecision).ToString("0.###", CultureInfo.InvariantCulture);
}
