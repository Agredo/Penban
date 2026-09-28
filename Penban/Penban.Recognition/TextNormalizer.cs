using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Penban.Recognition;

/// <summary>
/// Produces the searchable form of recognised text.
/// <para>
/// This exists because the recognizer emits characters that look right and are wrong. The Phase 0
/// benchmark produced <c>Exροr</c> with a Greek rho, <c>κόσμe</c> with a Cyrillic <c>е</c>, and
/// <c>Straβe</c> with a Greek beta where the ink said <c>ß</c>. The model saw the correct shape and
/// picked a codepoint from the wrong script, so a plain string comparison finds none of those words.
/// </para>
/// <para>
/// The fold is deliberately <em>visual</em>, not a transliteration: only characters whose glyph is
/// indistinguishable from a Latin letter are mapped. Transliterating all of Greek would repair the
/// confusions but destroy notes that are genuinely written in Greek, and those must stay findable in
/// Greek. Text that is legitimately Greek or Cyrillic keeps its script.
/// </para>
/// <para>
/// The same function must be applied to the query and to the stored text. Whether <c>ü</c> becomes
/// <c>u</c> matters far less than that both sides agree - which is also why German umlaut expansion
/// (<c>ü</c> to <c>ue</c>) is not applied: it would conflict with the base-letter fold, and the
/// consistent choice beats the clever one.
/// </para>
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Characters that only need to become a different single character. Keys are lower case; the
    /// caller lower-cases first, so the upper-case forms are covered without listing them.
    /// </summary>
    private static readonly FrozenDictionary<char, char> VisualEquivalents = new Dictionary<char, char>
    {
        // Cyrillic letters that are drawn identically to a Latin one.
        ['а'] = 'a',
        ['в'] = 'b',
        ['е'] = 'e',
        ['і'] = 'i',
        ['ј'] = 'j',
        ['к'] = 'k',
        ['м'] = 'm',
        ['н'] = 'h',
        ['о'] = 'o',
        ['р'] = 'p',
        ['с'] = 'c',
        ['т'] = 't',
        ['у'] = 'y',
        ['х'] = 'x',
        ['ѕ'] = 's',
        ['ԁ'] = 'd',
        ['һ'] = 'h',
        ['ӏ'] = 'l',

        // Greek letters that are drawn identically to a Latin one.
        ['α'] = 'a',
        ['ε'] = 'e',
        ['ι'] = 'i',
        ['κ'] = 'k',
        ['ν'] = 'v',
        ['ο'] = 'o',
        ['ρ'] = 'p',
        ['τ'] = 't',
        ['υ'] = 'u',
        ['χ'] = 'x',
        ['ϲ'] = 'c',
    }.ToFrozenDictionary();

    /// <summary>
    /// Characters that fold to more than one character. The Greek beta and the Latin sharp s are the
    /// same shape, and both stand for <c>ss</c> in German - which is exactly the confusion the
    /// benchmark hit on <c>Straβe</c>.
    /// </summary>
    private static readonly FrozenDictionary<char, string> MultiCharacterEquivalents =
        new Dictionary<char, string>
        {
            ['ß'] = "ss",
            ['β'] = "ss",
            ['ϐ'] = "ss",
        }.ToFrozenDictionary();

    /// <summary>
    /// Returns the searchable form of <paramref name="text"/>: NFC, lower case, accents dropped in the
    /// Latin and Greek scripts, visually equivalent characters folded to Latin, whitespace collapsed.
    /// Never <c>null</c>; returns the empty string for <c>null</c>, empty or whitespace-only input.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Decomposing first is what makes the accents removable: "ü" becomes "u" plus a combining
        // diaeresis, so the mark can be dropped and the base letter kept.
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        var baseCharacter = '\0';
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.NonSpacingMark)
            {
                // Marks are only dropped where they are decoration on a Latin or Greek base letter.
                // In Cyrillic they carry meaning - "й" is not "и" - so they survive.
                if (!IsMarkStrippableBase(baseCharacter))
                {
                    builder.Append(character);
                }

                continue;
            }

            if (category == UnicodeCategory.Control || category == UnicodeCategory.Format)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (builder.Length > 0)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            baseCharacter = character;
            var lower = char.ToLowerInvariant(character);

            if (MultiCharacterEquivalents.TryGetValue(lower, out var multi))
            {
                builder.Append(multi);
                continue;
            }

            if (VisualEquivalents.TryGetValue(lower, out var equivalent))
            {
                builder.Append(equivalent);
                continue;
            }

            builder.Append(lower);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a combining mark on <paramref name="baseCharacter"/> is decoration that can be dropped.
    /// </summary>
    private static bool IsMarkStrippableBase(char baseCharacter) =>
        baseCharacter <= '\u024F' && baseCharacter >= '\u0041'   // Basic Latin through Latin Extended-B
        || baseCharacter is >= '\u0370' and <= '\u03FF'           // Greek and Coptic
        || baseCharacter is >= '\u1F00' and <= '\u1FFF';          // Greek Extended
}
