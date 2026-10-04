using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// The board a newly placed widget should start on.
/// <para>
/// iOS places a widget for the user, never for the app - there is no call that puts one on the home
/// screen. So the board can only be noted: it is written into the snapshot as its default, and the
/// sheet that adds the widget then starts on it instead of on the first board of the overview.
/// </para>
/// <para>
/// Nothing in the app writes the preference any more: the board used to be picked by holding down a
/// board card, which is what the widget hint explained, and the widget's own configuration sheet has
/// taken that over. A value left behind by an older version is still honoured.
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
}
