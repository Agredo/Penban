namespace Penban.Maui.Views.Widget;

/// <summary>
/// The board a tapped home screen widget stands for, on its way from the tap to the overview.
/// <para>
/// The tap reaches the app through the URL handler, which is not a place a board can be opened from:
/// at that moment the app is still in the background, and after a cold start there is no overview
/// yet either. So the board only waits here until the app is up, and whichever gets there first opens
/// it - the activation that follows the tap, or the overview when it appears (see
/// <c>BoardsPage.OnAppearing</c>).
/// </para>
/// </summary>
public sealed class WidgetBoardLink
{
    private Guid? boardId;

    /// <summary>Notes the board the tapped widget stands for.</summary>
    public void Request(Guid id)
    {
        boardId = id;
    }

    /// <summary>
    /// Takes the request, if it still stands. Reading it also clears it, so a tap leads to its board
    /// once, however many of the two moments see it - and so that a request that was answered by a
    /// board that no longer exists is not waited on again.
    /// </summary>
    public Guid? Take()
    {
        var taken = boardId;
        boardId = null;
        return taken;
    }
}
