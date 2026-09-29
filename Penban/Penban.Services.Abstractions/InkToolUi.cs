namespace Penban.Services.Abstractions;

/// <summary>
/// Selects whether the drawing tools of the ink editor are also reached from the rings of the radial
/// menu, next to the row of buttons and colours above the note. The row is drawn either way, because
/// it shares its row with the buttons that leave the card: hiding it would take no height off the
/// note and would only put the tools out of reach. Lives next to
/// <see cref="PreferenceKeys.InkToolUi"/> because it is the value stored under that key, and the
/// settings page has to be able to offer it without referencing the MAUI view layer.
/// </summary>
public enum InkToolUi
{
    /// <summary>
    /// The row of tools and the two palettes, and the ring on top of it: a free finger taps the note
    /// to ask for one, and the button beside the row is what is left when there is no free finger or
    /// no right mouse button.
    /// </summary>
    RadialMenu,

    /// <summary>
    /// The tools and the two palettes in the row above the note, with no radial menu: neither the tap
    /// on the note nor the right mouse button asks for one.
    /// </summary>
    ToolBar,
}
