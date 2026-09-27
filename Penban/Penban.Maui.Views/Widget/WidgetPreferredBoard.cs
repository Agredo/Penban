using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// The board a widget should start on, as picked by holding down a board card.
/// <para>
/// iOS places a widget for the user, never for the app - there is no call that puts one on the home
/// screen. So the gesture cannot do more than note the wish: the board is remembered here, written
/// into the snapshot as its default, and the sheet that adds the widget then starts on it instead of
/// on the first board of the overview.
/// </para>
/// </summary>
public static class WidgetPreferredBoard
{
    /// <summary>
    /// The board a newly placed widget should start on, or null when none was picked or the pick is
    /// no longer readable. A board that was deleted since is not filtered out here: the widget falls
    /// back to the first board of the overview by itself.
    /// </summary>
    public static Guid? Get(IPreferences? preferences) =>
        Guid.TryParse(preferences?.Get(PreferenceKeys.WidgetBoard, string.Empty), out var id) ? id : null;

    /// <summary>Remembers a board as the one a newly placed widget should start on.</summary>
    public static void Set(IPreferences preferences, Guid boardId) =>
        preferences.Set(PreferenceKeys.WidgetBoard, boardId.ToString());
}
