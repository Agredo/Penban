namespace Penban.Maui.Views.Ink;

/// <summary>
/// Where the finger of a swipe out of the editor is, in <see cref="InkDocument"/> units: how far
/// down it has come since it landed (<see cref="Travel"/>) and where on the note it landed
/// (<see cref="StartY"/>, counted from the top of the note).
/// <para>
/// Both are reported, not only the travel, because the page has to know where on the screen the
/// finger is: how far a card has to be dragged before letting go closes the editor is a question of
/// how far down the screen the finger has come, and a finger that started low has less of the screen
/// left to cross than one that started high.
/// </para>
/// </summary>
/// <param name="Travel">Distance travelled down the note since the finger landed. Negative upwards.</param>
/// <param name="StartY">Where the finger landed, measured down from the top of the note.</param>
public readonly record struct CloseSwipeMove(double Travel, double StartY);
