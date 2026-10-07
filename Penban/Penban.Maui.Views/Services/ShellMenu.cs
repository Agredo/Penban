namespace Penban.Maui.Views.Services;

/// <summary>
/// The menu that leads to the three areas, opened from the button in the head of each of them.
/// <para>
/// Shell draws a button of its own into its navigation bar, but that bar is hidden everywhere in this
/// app: it shows no back arrow on Windows, so every page draws its own head instead (see
/// <c>BoardsPage</c>). A hidden bar takes its menu button with it, and a flyout that cannot be opened
/// is a flyout that does not exist - so the three areas ask for it here.
/// </para>
/// </summary>
public static class ShellMenu
{
    /// <summary>Shows the menu. Nothing happens where there is no shell to show it on.</summary>
    public static void Open()
    {
        if (Shell.Current is { } shell)
        {
            shell.FlyoutIsPresented = true;
        }
    }
}
