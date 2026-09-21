// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// The app's settings. Each setting is a plain property whose setter writes straight through to
/// <see cref="IPreferences"/>, so there is no "apply" step and nothing to lose if the page is
/// closed without confirming.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Order of <see cref="RendererNames"/>; index in this array is the picked value.</summary>
    private static readonly InkRenderer[] Renderers =
    [
        InkRenderer.Skia,
        InkRenderer.CommunityToolkitDrawingView,
    ];

    private readonly IPreferences preferences;

    /// <summary>Suppresses the write-through while <see cref="Refresh"/> is reading, so loading the
    /// page does not write back every value it just read.</summary>
    private bool isLoading;

    public SettingsViewModel(IPreferences preferences)
    {
        this.preferences = preferences;
        Refresh();
    }

    /// <summary>Display names of the ink renderers, in the order of <see cref="SelectedRendererIndex"/>.</summary>
    public IReadOnlyList<string> RendererNames { get; } = [Strings.InkRendererSkia, Strings.InkRendererToolkit];

    /// <summary>
    /// Re-reads every setting from the store. Settings can also be changed outside this page (the
    /// ink editor has an inline finger-drawing toggle), so the page refreshes on appearing rather
    /// than trusting the values it was constructed with.
    /// </summary>
    public void Refresh()
    {
        isLoading = true;
        try
        {
            AllowFingerDrawing = ReadBool(PreferenceKeys.AllowFingerDrawing, true);
            PencilDoubleTapEnabled = ReadBool(PreferenceKeys.PencilDoubleTapEnabled, true);
            PenTailEraserEnabled = ReadBool(PreferenceKeys.PenTailEraserEnabled, true);
            PressureSensitiveWidth = ReadBool(PreferenceKeys.PressureSensitiveWidth, true);
            AutoSizeCards = ReadBool(PreferenceKeys.AutoSizeCards, false);
            TiltDetectionEnabled = ReadBool(PreferenceKeys.TiltDetectionEnabled, false);
            TiltRenderingEffect = ReadBool(PreferenceKeys.TiltRenderingEffect, false);
            SelectedRendererIndex = ReadRendererIndex();
        }
        finally
        {
            isLoading = false;
        }

        OnPropertyChanged(string.Empty);
    }

    [ObservableProperty]
    private bool allowFingerDrawing;

    partial void OnAllowFingerDrawingChanged(bool value) => Persist(PreferenceKeys.AllowFingerDrawing, value);

    [ObservableProperty]
    private bool pencilDoubleTapEnabled;

    partial void OnPencilDoubleTapEnabledChanged(bool value) => Persist(PreferenceKeys.PencilDoubleTapEnabled, value);

    /// <summary>
    /// Erasing with the eraser end of the pen. Only Windows can tell the two ends of a pen apart,
    /// so the settings page hides the row everywhere else - the value is still stored, which keeps
    /// it from being reset when the same store is used on a device that cannot use it.
    /// </summary>
    [ObservableProperty]
    private bool penTailEraserEnabled;

    partial void OnPenTailEraserEnabledChanged(bool value) => Persist(PreferenceKeys.PenTailEraserEnabled, value);

    [ObservableProperty]
    private bool pressureSensitiveWidth;

    partial void OnPressureSensitiveWidthChanged(bool value) => Persist(PreferenceKeys.PressureSensitiveWidth, value);

    /// <summary>Which ink renderer a card's drawing surface uses.</summary>
    [ObservableProperty]
    private int selectedRendererIndex;

    partial void OnSelectedRendererIndexChanged(int value)
    {
        if (!isLoading && value >= 0 && value < Renderers.Length)
        {
            preferences.Set(PreferenceKeys.InkRenderer, Renderers[value].ToString());
        }
    }

    [ObservableProperty]
    private bool tiltDetectionEnabled;

    partial void OnTiltDetectionEnabledChanged(bool value) => Persist(PreferenceKeys.TiltDetectionEnabled, value);

    /// <summary>
    /// Depends on <see cref="TiltDetectionEnabled"/>: without tilt data there is nothing for the
    /// effect to render, so the page disables this switch while that one is off.
    /// </summary>
    [ObservableProperty]
    private bool tiltRenderingEffect;

    partial void OnTiltRenderingEffectChanged(bool value) => Persist(PreferenceKeys.TiltRenderingEffect, value);

    [ObservableProperty]
    private bool autoSizeCards;

    partial void OnAutoSizeCardsChanged(bool value) => Persist(PreferenceKeys.AutoSizeCards, value);

    private void Persist(string key, bool value)
    {
        if (!isLoading)
        {
            preferences.Set(key, value.ToString());
        }
    }

    private int ReadRendererIndex()
    {
        var stored = preferences.Get(PreferenceKeys.InkRenderer, InkRenderer.Skia.ToString());
        var renderer = Enum.TryParse<InkRenderer>(stored, out var parsed) ? parsed : InkRenderer.Skia;
        return Math.Max(Array.IndexOf(Renderers, renderer), 0);
    }

    private bool ReadBool(string key, bool @default) =>
        bool.TryParse(preferences.Get(key, @default.ToString()), out var value) ? value : @default;
}
