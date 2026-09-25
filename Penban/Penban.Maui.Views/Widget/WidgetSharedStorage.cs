namespace Penban.Maui.Views.Widget;

/// <summary>
/// The folder the app and the home screen widget share.
/// <para>
/// A widget runs in its own process and reaches none of the app's own folders, so both sides meet in
/// an App Group container: the app writes the snapshot and the previews there, the widget reads them.
/// The group has to be registered in the Apple Developer Portal and claimed by both targets; without
/// it the container does not exist and nothing is written at all.
/// </para>
/// </summary>
public static class WidgetSharedStorage
{
    /// <summary>Identifier of the App Group, as registered in the Apple Developer Portal.</summary>
    public const string GroupId = "group.com.agredoapplication.panban";

    private static string? directory;

    /// <summary>
    /// The shared folder, or null where there is no widget to feed - every platform but iOS. A folder
    /// that cannot be resolved yet is asked for again on the next call rather than remembered.
    /// </summary>
    public static string? Directory => directory ??= Resolve();

    private static string? Resolve()
    {
#if IOS
        try
        {
            return Foundation.NSFileManager.DefaultManager.GetContainerUrl(GroupId)?.Path;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"App group container unavailable: {exception.Message}");
            return null;
        }
#else
        return null;
#endif
    }
}
