using Microsoft.Maui.Storage;
using Penban.Services.Abstractions;

namespace Penban.Maui.Services;

/// <summary>MAUI-backed implementation of <see cref="Penban.Services.Abstractions.IPreferences"/>, wrapping <see cref="Preferences.Default"/>.</summary>
public class MauiPreferences : Penban.Services.Abstractions.IPreferences
{
    public string Get(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}
