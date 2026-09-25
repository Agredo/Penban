using System.Diagnostics;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Asks the home screen to draw the widget again.
/// <para>
/// WidgetKit is not part of the .NET bindings, so the single call that is needed -
/// <c>reloadAllTimelines</c> on <c>WidgetCenter.shared</c> - is sent through the Objective-C runtime
/// by hand. Nothing depends on it: a widget whose timeline has run out reloads itself, and the
/// snapshot is on disk either way, so the new picture only arrives a little later.
/// </para>
/// </summary>
public static class IosWidgetRefresh
{
    public static void ReloadAllTimelines()
    {
#if IOS
        // Sent from the main thread, where the rest of the app talks to the system frameworks.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var widgetCenter = ObjCRuntime.Class.GetHandle("WidgetCenter");
                if (widgetCenter == IntPtr.Zero)
                {
                    // iOS before 14: there is no widget to reload.
                    return;
                }

                var shared = ObjCRuntime.Messaging.IntPtr_objc_msgSend(widgetCenter, ObjCRuntime.Selector.GetHandle("sharedCenter"));
                if (shared == IntPtr.Zero)
                {
                    return;
                }

                ObjCRuntime.Messaging.void_objc_msgSend(shared, ObjCRuntime.Selector.GetHandle("reloadAllTimelines"));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Widget reload not requested: {exception.Message}");
            }
        });
#endif
    }
}
