namespace Penban.Util;

/// <summary>
/// The letters written on a project's card as long as the project carries no note of its own: the
/// first letters of its first two words, so that a project without a note still has something of its
/// own on the card rather than a blank sheet.
/// </summary>
public static class Monogram
{
    /// <summary>How many words the monogram takes a letter from.</summary>
    private const int LetterCount = 2;

    /// <summary>
    /// The monogram of a title, uppercased: <c>"Urlaub Planung"</c> reads <c>UP</c>. A title of one
    /// word gives its first letter alone, and an empty title gives nothing at all - the card then
    /// shows a blank note, which is what a board without a note shows too.
    /// </summary>
    public static string From(string? title)
    {
        var letters = new List<char>(LetterCount);

        foreach (var word in (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (letters.Count == LetterCount)
            {
                break;
            }

            letters.Add(char.ToUpperInvariant(word[0]));
        }

        return new string(letters.ToArray());
    }
}
