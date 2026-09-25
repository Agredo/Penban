using Microsoft.Maui.Graphics;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// The handful of design tokens the widget card is drawn with, taken from
/// <c>Resources/Styles/Colors.xaml</c> and <c>Theme.xaml</c>.
/// <para>
/// The widget cannot resolve the app's resource dictionaries - it is a separate process, and the
/// images are drawn before it ever runs - so the tokens it needs are copied here. They have to be
/// kept in step with the XAML by hand; a token that changes there and not here shows up as a card
/// that no longer matches the app.
/// </para>
/// </summary>
/// <param name="Background">Behind the card; the widget itself has no surface of its own.</param>
/// <param name="Surface">Fill of the card, see the <c>CardBorder</c> style.</param>
/// <param name="Border">Hairline around the card.</param>
/// <param name="TextPrimary">Board title.</param>
/// <param name="TextSecondary">Caption under the title.</param>
/// <param name="ChipInk">Number on a lane chip; dark in both appearances, since the paper stays bright.</param>
/// <param name="ShadowAlpha">Strength of the card's shadow; the dark theme lifts the card more.</param>
/// <param name="IsDark">Whether this is the dark appearance, which picks the file the widget shows.</param>
public sealed record WidgetPalette(
    Color Background,
    Color Surface,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color ChipInk,
    float ShadowAlpha,
    bool IsDark)
{
    /// <summary>The light appearance, drawn when the home screen is not in dark mode.</summary>
    public static WidgetPalette Light { get; } = new(
        Background: Color.FromArgb("#F7F7F8"),
        Surface: Color.FromArgb("#FFFFFF"),
        Border: Color.FromArgb("#E4E4E8"),
        TextPrimary: Color.FromArgb("#17171A"),
        TextSecondary: Color.FromArgb("#6E6E78"),
        ChipInk: Color.FromArgb("#1E2230"),
        ShadowAlpha: 0.07f,
        IsDark: false);

    /// <summary>The dark appearance.</summary>
    public static WidgetPalette Dark { get; } = new(
        Background: Color.FromArgb("#0F1115"),
        Surface: Color.FromArgb("#171A21"),
        Border: Color.FromArgb("#262B36"),
        TextPrimary: Color.FromArgb("#EDEDF0"),
        TextSecondary: Color.FromArgb("#8A90A2"),
        ChipInk: Color.FromArgb("#1E2230"),
        ShadowAlpha: 0.35f,
        IsDark: true);

    /// <summary>Both appearances, in the order the file names are built.</summary>
    public static IReadOnlyList<WidgetPalette> All { get; } = [Light, Dark];
}
