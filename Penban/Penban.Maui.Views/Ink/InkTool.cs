namespace Penban.Maui.Views.Ink;

/// <summary>
/// What a contact on the writing surface does. The three tools are alternatives, not layers: the
/// surface is always in exactly one of them, which is what makes a single press unambiguous - the
/// pen draws, the eraser takes strokes away, the lasso picks strokes up.
/// <para>
/// A tool that is only meant to last as long as a pen button is held down is not one of these: that
/// is <see cref="IInkCanvasView.Tool"/> being changed for the length of the press and put back
/// afterwards, which is what the platform behaviors that read a pen's own controls do.
/// </para>
/// </summary>
public enum InkTool
{
    /// <summary>Writes: a contact draws a stroke.</summary>
    Pen,

    /// <summary>Takes away: a contact removes the whole strokes it passes over.</summary>
    Eraser,

    /// <summary>
    /// Selects: a contact draws a loop, and the strokes inside it are picked up - a drag carries
    /// them somewhere else, and the toolbar can take them away.
    /// </summary>
    Lasso,
}
