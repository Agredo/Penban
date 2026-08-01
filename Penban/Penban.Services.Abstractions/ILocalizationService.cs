using System.Globalization;

namespace Penban.Services.Abstractions;

/// <summary>
/// Manages the app's active display language, independent of the OS UI language.
/// Selection is persisted via <see cref="IPreferences"/> and available languages are
/// discovered dynamically from the resource satellite assemblies (Penban.Util),
/// not hard-coded.
/// </summary>
public interface ILocalizationService
{
    CultureInfo CurrentLanguage { get; }

    IReadOnlyList<CultureInfo> AvailableLanguages { get; }

    Task SetLanguageAsync(CultureInfo culture);

    /// <summary>Raised whenever <see cref="CurrentLanguage"/> changes, so bound UI can refresh.</summary>
    event EventHandler? LanguageChanged;
}
