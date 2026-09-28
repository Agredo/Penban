using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Asks the home screen to draw the widget again.
/// <para>
/// WidgetKit is not part of the .NET bindings and has no Objective-C class either - it is a Swift
/// framework - so there is nothing to message from here. The app therefore carries a small Swift
/// object, <c>PenbanWidgetReloader</c> (Platforms/iOS/PenbanWidgetReloader.swift, compiled into the
/// app by Penban.Maui.csproj), whose class method does the one call that is needed; this is where that
/// object is asked. Without it the widget only changes when its own timeline runs out, which can take
/// an hour.
/// </para>
/// <para>
/// Nothing depends on the call going through: the snapshot is on disk either way, so a widget that is
/// not reloaded only shows the new picture a little later.
/// </para>
/// </summary>
public static class IosWidgetRefresh
{
#if IOS
    // The Objective-C runtime is called by hand because WidgetKit has no .NET binding.
    [DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend(IntPtr receiver, IntPtr selector);

    // The name the Swift object is registered under, spelled out in @objc(PenbanWidgetReloader).
    private const string ReloaderClassName = "PenbanWidgetReloader";
#endif

    public static void ReloadAllTimelines()
    {
#if IOS
        // Sent from the main thread, where the rest of the app talks to the system frameworks.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // Without the Swift object - an iOS build that did not link it - there is nothing to
                // ask. Nothing is lost: the widget reloads when its own timeline runs out.
                var reloader = ObjCRuntime.Class.GetHandle(ReloaderClassName);
                if (reloader == IntPtr.Zero)
                {
                    Debug.WriteLine($"Widget reload not requested: {ReloaderClassName} is not in this build.");
                    return;
                }

                // A class method, so the receiver is the class object itself - there is no instance
                // to ask for.
                void_objc_msgSend(reloader, ObjCRuntime.Selector.GetHandle("reloadAllTimelines"));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Widget reload not requested: {exception.Message}");
            }
        });
#endif
    }
}
