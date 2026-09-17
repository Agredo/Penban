namespace Penban.Util;

/// <summary>
/// Derives the paper colour and the slight tilt of a card from its id, so a note always looks
/// the same - the way a note that was physically stuck to the board would. Only primitives are
/// produced here; the view layer owns the actual colours.
/// </summary>
public static class NoteStyle
{
    /// <summary>Number of paper colours the view layer offers.</summary>
    public const int PaperCount = 6;

    /// <summary>Note tilts in degrees; small enough to stay readable, uneven enough to look stuck on by hand.</summary>
    private static readonly double[] Tilts = [-2.2, 1.4, -1.6, 2.6, -1.0, 1.8, -2.8, 0.9];

    /// <summary>Index of the paper colour to use for this note.</summary>
    public static int PaperIndexFor(Guid id) => id.ToByteArray()[0] % PaperCount;

    /// <summary>Tilt of this note in degrees.</summary>
    public static double TiltFor(Guid id) => Tilts[id.ToByteArray()[1] % Tilts.Length];
}
