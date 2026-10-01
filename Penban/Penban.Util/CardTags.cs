namespace Penban.Util;

/// <summary>
/// The tags a card can carry: small emoji pinned to the note. A tag is stored as the emoji itself,
/// so a card keeps its tags when the set offered here changes.
/// </summary>
public static class CardTags
{
    /// <summary>The tags the editor offers.</summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "⭐", "❗", "❓", "💡", "✅", "🔥", "📌", "⏰", "❤️", "🎯", "🐞", "💬",
    ];

    /// <summary>Most tags one card can carry, so the row on the note never outgrows it.</summary>
    public const int MaxPerCard = 4;

    /// <summary>Toggles <paramref name="tag"/> in <paramref name="tags"/>; returns false when the card is full.</summary>
    public static bool Toggle(List<string> tags, string tag)
    {
        if (tags.Remove(tag))
        {
            return true;
        }

        if (tags.Count >= MaxPerCard)
        {
            return false;
        }

        tags.Add(tag);
        return true;
    }

    /// <summary>The tags as one line, as they are drawn on the note.</summary>
    public static string Join(IEnumerable<string> tags) => string.Concat(tags);
}
