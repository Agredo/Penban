namespace Penban.Util;

/// <summary>
/// Labels on projects and boards, in the shape the rest of the app expects them: trimmed, without
/// empty entries and without duplicates. The labels are free text, so the same one can arrive as
/// <c>"Urlaub"</c>, <c>"urlaub "</c> and <c>"urlaub"</c> - all three have to end up as a single
/// label, or the filter row would offer three chips that mean the same thing.
/// </summary>
public static class TagList
{
    /// <summary>
    /// Characters a run of typed labels is split on, so that the whole row can be written into one
    /// field: <c>urlaub, 2026; wichtig</c> is three labels.
    /// </summary>
    private static readonly char[] Separators = [',', ';', '\n', '\r', '\t'];

    /// <summary>
    /// Trims the labels, drops the empty ones and removes duplicates, keeping the first spelling of
    /// each - so what the user wrote first is what a chip reads. The order the labels were given in
    /// is kept, which is the order the chips are shown in.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        if (tags is null)
        {
            return result;
        }

        foreach (var tag in tags)
        {
            var trimmed = tag?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (!result.Contains(trimmed, StringComparer.CurrentCultureIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a run of typed labels into the list shape <see cref="Normalize"/> produces. A label that
    /// carries a separator of its own cannot be typed here - the field holds the whole row - which is
    /// why the labels of a card are typed one at a time and only this row is split.
    /// </summary>
    public static List<string> Parse(string? text)
        => Normalize(text?.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
}
