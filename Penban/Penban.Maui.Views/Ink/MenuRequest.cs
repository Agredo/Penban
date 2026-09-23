namespace Penban.Maui.Views.Ink;

/// <summary>
/// Where on the note the menu was asked for, in <see cref="InkDocument"/> units: the point a finger
/// was tapped at, or the point the right mouse button was pressed at.
/// <para>
/// The point is handed over rather than kept, because it is the only thing the surface knows about
/// the menu. Which blocks the menu offers and where it may stand are the page's, and the page turns
/// this point into a place on the screen through the same conversion it uses for the strokes.
/// </para>
/// </summary>
/// <param name="X">Distance from the left edge of the note.</param>
/// <param name="Y">Distance from the top edge of the note.</param>
public readonly record struct MenuRequest(double X, double Y);
