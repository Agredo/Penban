using System.Globalization;

namespace Penban.Util;

/// <summary>Small collection of shared helpers used across the app's non-UI layers.</summary>
public static class LocalizationHelper
{
    /// <summary>
    /// Discovers the cultures for which a satellite resource assembly exists next to the
    /// currently executing assembly, plus the neutral (English) culture. This avoids
    /// hard-coding the list of supported languages: adding a new <c>Strings.xx.resx</c>
    /// file is enough to make a language available.
    /// </summary>
    public static IReadOnlyList<CultureInfo> DiscoverAvailableLanguages()
    {
        var neutral = CultureInfo.GetCultureInfo("en");
        var languages = new List<CultureInfo> { neutral };

        var baseDirectory = AppContext.BaseDirectory;
        if (Directory.Exists(baseDirectory))
        {
            foreach (var directory in Directory.GetDirectories(baseDirectory))
            {
                var cultureName = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(cultureName))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, "Penban.Util.resources.dll")))
                {
                    continue;
                }

                try
                {
                    languages.Add(CultureInfo.GetCultureInfo(cultureName));
                }
                catch (CultureNotFoundException)
                {
                    // Not a culture-named directory; ignore.
                }
            }
        }

        return languages;
    }
}
