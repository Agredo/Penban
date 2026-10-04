namespace Penban.Models;

/// <summary>
/// How the typed text of a note is written: how large it may be, how large it starts and how that
/// size turns into the size the platform's text stack is given.
/// <para>
/// The size is kept in document units - the same unit <see cref="InkDocument"/> and
/// <see cref="InkStroke.Thickness"/> use - and converted wherever it is drawn, so "size 48" is the
/// same share of the note in the editor, on the board's preview and on a phone. This is the one
/// place that conversion is written down, and the one place the range is.
/// </para>
/// </summary>
public static class CardText
{
    /// <summary>Smallest text: still smaller than the thinnest pen, but readable on a phone.</summary>
    public const float Minimum = 20f;

    /// <summary>Largest text: a note filled by a headline, not a size for writing a paragraph in.</summary>
    public const float Maximum = 160f;

    /// <summary>Size a note starts with when the user never chose one.</summary>
    public const float Default = 48f;

    /// <summary>
    /// Step the toolbar's slider walks. The slider is dragged rather than picked from a ladder, so
    /// this only keeps the read-out number from flickering through every fraction in between.
    /// </summary>
    public const float SliderStep = 2f;

    /// <summary>
    /// How much larger the title is than the body. Fixed rather than a second size to set: a note
    /// has one size, and a title that is half again as large is what makes it read as a title.
    /// </summary>
    public const double TitleScale = 1.5;

    /// <summary>Colour typed text starts in, and the colour used when the stored one cannot be read.</summary>
    public const string DefaultColorHex = "#000000";

    /// <summary>Keeps a stored size inside the range text can be set to.</summary>
    public static float Clamp(float size) =>
        float.IsFinite(size) ? Math.Clamp(size, Minimum, Maximum) : Default;

    /// <summary>Rounds a size onto the nearest step of the given width, and keeps it in range.</summary>
    public static float Snap(float size, float step) =>
        step <= 0f ? Clamp(size) : Clamp(Minimum + (MathF.Round((size - Minimum) / step) * step));

    /// <summary>Size the title is drawn at, as a multiple of the size the note is set to.</summary>
    public static double TitleSize(float size) => Clamp(size) * TitleScale;

    /// <summary>
    /// The size in device-independent units - what a MAUI label or editor is given - for a note drawn
    /// <paramref name="side"/> units across. A square of <see cref="InkDocument.Size"/> units is drawn
    /// as that side, which is the same factor the ink is drawn with, so text and strokes stay in
    /// proportion however large the note is on screen.
    /// </summary>
    public static double FontSizeFor(float size, double side) =>
        side <= 0 ? 0 : Clamp(size) / InkDocument.Size * side;

    /// <summary>
    /// The size the title is drawn at on screen, worked out the way the body's is and then made as
    /// much larger as a title is - see <see cref="TitleScale"/>. Not <see cref="FontSizeFor"/> of
    /// <see cref="TitleSize"/>: that would clamp the title back down to the largest body size.
    /// </summary>
    public static double TitleFontSizeFor(float size, double side) =>
        side <= 0 ? 0 : TitleSize(size) / InkDocument.Size * side;

    /// <summary>
    /// The colour the text is written in, as <c>#RRGGBB</c>. A colour that cannot be read - a stored
    /// value from an older or hand-edited file - falls back on the default instead of drawing nothing
    /// the user could see.
    /// </summary>
    public static string ColorHexOf(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return PenColor.TryNormalize(card.TextColorHex, out var hex) ? hex : DefaultColorHex;
    }

    /// <summary>
    /// The size a note's text is written at, read off the card. A card that was never given one - the
    /// field is <c>0</c> on a card from before text existed, and on every brand new card - gets the
    /// default rather than the smallest size there is.
    /// </summary>
    public static float SizeOf(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.TextSize > 0f ? Clamp(card.TextSize) : Default;
    }

    /// <summary>Whether the note carries any typed text at all. Whitespace is not text.</summary>
    public static bool HasText(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return !string.IsNullOrWhiteSpace(card.TextTitle) || !string.IsNullOrWhiteSpace(card.TextBody);
    }

    /// <summary>
    /// The title and the body as the one text a search reads: the title first, then a blank line, then
    /// the body. A card is found by what is written on it, not by which of the two fields it sits in,
    /// so both halves go into the index as one; the blank line is what keeps a title from running into
    /// the body when it is shown as the text that was matched.
    /// </summary>
    public static string Flatten(string? title, string? body)
    {
        var heading = (title ?? string.Empty).Trim();
        var text = (body ?? string.Empty).Trim();

        if (heading.Length == 0)
        {
            return text;
        }

        return text.Length == 0 ? heading : $"{heading}\n\n{text}";
    }

    /// <inheritdoc cref="Flatten(string?, string?)" />
    public static string Flatten(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return Flatten(card.TextTitle, card.TextBody);
    }
}
