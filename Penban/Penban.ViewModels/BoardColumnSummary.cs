// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Globalization;

namespace Penban.ViewModels;

/// <summary>
/// One lane of the "how full is this board" bar on the board overview. The chip is as wide as the
/// lane's share of the board's cards, so the overview answers "where is the work piled up?" without
/// opening the board.
/// </summary>
public sealed class BoardColumnSummary
{
    public BoardColumnSummary(string title, int cardCount, int noteColorIndex)
    {
        Title = title;
        CardCount = cardCount;
        NoteColorIndex = noteColorIndex;

        // An empty lane keeps a share of the bar as well: that a lane exists but is empty is
        // information too, it just must not look as heavy as a full one. The share is capped so a
        // single very full lane cannot squeeze the others down to an unreadable sliver.
        Share = Math.Min(cardCount, 7) + 1;
    }

    /// <summary>Lane title, as shown on the board itself.</summary>
    public string Title { get; }

    public int CardCount { get; }

    /// <summary>Card count as it is written on the chip.</summary>
    public string CountText => CardCount.ToString(CultureInfo.CurrentCulture);

    /// <summary>Paper colour of the board this lane belongs to, so the bar matches its notes.</summary>
    public int NoteColorIndex { get; }

    /// <summary>Relative width of the chip; grows with the number of cards in the lane.</summary>
    public float Share { get; }

    /// <summary>An empty lane is drawn faint, so the bar shows where the work sits at a glance.</summary>
    public double ChipOpacity => CardCount == 0 ? 0.45 : 1;
}
