namespace Penban.Maui.Views;

/// <summary>
/// The app's own version numbers in the form they are shown to the user. Single source of truth for
/// the About page and for the version that travels with a feedback report, so the two can never
/// drift apart.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// The package version as the user should see it. Windows appends the build number to the
    /// package version ("0.5.5" becomes "0.5.5.18") although the build is shown on its own, and a
    /// package that never set a version carries trailing zeros ("1.0.0.0"); both are dropped, and
    /// the first part always stays.
    /// </summary>
    public static string Display
    {
        get
        {
            var parts = AppInfo.Current.VersionString.Split('.');
            var last = parts.Length - 1;

            if (last > 0 && parts[last] == Build)
            {
                last--;
            }

            while (last > 0 && parts[last] == "0")
            {
                last--;
            }

            return string.Join('.', parts, 0, last + 1);
        }
    }

    /// <summary>The build number, which is what tells two uploads of the same version apart.</summary>
    public static string Build => AppInfo.Current.BuildString;
}
