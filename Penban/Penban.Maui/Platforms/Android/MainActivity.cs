using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Penban.Maui.Views.Ink;

namespace Penban.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Sees every touch in the app before it is dispatched to the view it belongs to, which is the
    /// only place the pen's tilt and the two- and three-finger taps can be read without taking the
    /// ink input away from the drawing surface - see <see cref="AndroidInkInput"/>. The event is
    /// only looked at, never consumed.
    /// </summary>
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        AndroidInkInput.Publish(e);
        return base.DispatchTouchEvent(e);
    }
}
