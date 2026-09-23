namespace Penban.Services.Abstractions;

/// <summary>
/// Selects how the drawing tools of the ink editor are reached: from the rings of the radial menu or
/// from the row of buttons and colours above the note. The menu offers what the tools and the two
/// palettes offer, so the two are alternatives and only the chosen one is shown. Lives next to
/// <see cref="PreferenceKeys.InkToolUi"/> because it is the value stored under that key, and the
/// settings page has to be able to offer it without referencing the MAUI view layer.
/// </summary>
public enum InkToolUi
{
    /// <summary>
    /// The radial menu, opened by tapping the note with a finger or with the right mouse button. The
    /// buttons and the colours are hidden, so the note is the only thing on the page to touch; the
    /// way out of the card and the button that opens the ring stay where they are, because the ring
    /// carries neither.
    /// </summary>
    RadialMenu,

    /// <summary>
    /// The tools and the two palettes in the row above the note, with no radial menu: neither the tap
    /// on the note nor the right mouse button asks for one.
    /// </summary>
    ToolBar,
}
