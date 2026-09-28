// The one thing the app needs from WidgetKit and cannot do itself.
//
// WidgetKit is a Swift framework: it has no Objective-C class that managed code could message, so
// "draw the widget again" has no binding on the .NET side. This object is compiled into the app
// executable by a target in Penban.Maui.csproj and is asked through the Objective-C runtime by
// Penban.Maui.Views/Widget/IosWidgetRefresh.cs - which looks the class up by the name below, spelled
// out with @objc so that it is not mangled. The method is a class method, so nothing has to be
// instantiated on this side.
import WidgetKit

@objc(PenbanWidgetReloader)
public final class PenbanWidgetReloader: NSObject {
    /// Asks the home screen to draw every widget of this app again.
    @objc public static func reloadAllTimelines() {
        WidgetCenter.shared.reloadAllTimelines()
    }
}
