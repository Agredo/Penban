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

    /// <summary>Order of <see cref="InkToolUiNames"/>; index in this array is the picked value.</summary>
    private static readonly InkToolUi[] ToolUis =
    [
        InkToolUi.RadialMenu,
        InkToolUi.ToolBar,
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
    /// Display names of the two ways of reaching the drawing tools, in the order of
    /// <see cref="SelectedInkToolUiIndex"/>.
    /// </summary>
    public IReadOnlyList<string> InkToolUiNames { get; } = [Strings.InkToolUiRadialMenu, Strings.InkToolUiToolBar];

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
            SelectedInkToolUiIndex = ReadInkToolUiIndex();
            PencilDoubleTapEnabled = ReadBool(PreferenceKeys.PencilDoubleTapEnabled, true);
            PenTailEraserEnabled = ReadBool(PreferenceKeys.PenTailEraserEnabled, true);
            PressureSensitiveWidth = ReadBool(PreferenceKeys.PressureSensitiveWidth, true);
            AutoSizeCards = ReadBool(PreferenceKeys.AutoSizeCards, false);
            TiltDetectionEnabled = ReadBool(PreferenceKeys.TiltDetectionEnabled, false);
            TiltRenderingEffect = ReadBool(PreferenceKeys.TiltRenderingEffect, false);
            TwoFingerTapUndoEnabled = ReadBool(PreferenceKeys.TwoFingerTapUndoEnabled, true);
            ThreeFingerTapRedoEnabled = ReadBool(PreferenceKeys.ThreeFingerTapRedoEnabled, true);
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

    /// <summary>
    /// How the drawing tools are reached, as an index into <see cref="ToolUis"/>. The radial menu is
    /// the default, because it is the one that leaves the whole page to the note.
    /// </summary>
    [ObservableProperty]
    private int selectedInkToolUiIndex;

    partial void OnSelectedInkToolUiIndexChanged(int value)
    {
        if (!isLoading && value >= 0 && value < ToolUis.Length)
        {
            preferences.Set(PreferenceKeys.InkToolUi, ToolUis[value].ToString());
        }
    }

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

    /// <summary>
    /// Two- and three-finger taps on the writing surface are shortcuts for the undo and redo
    /// buttons. On by default, because a tap that is not wanted is simply never made, while a
    /// gesture that has to be switched on first is a gesture nobody finds.
    /// </summary>
    [ObservableProperty]
    private bool twoFingerTapUndoEnabled;

    partial void OnTwoFingerTapUndoEnabledChanged(bool value) =>
        Persist(PreferenceKeys.TwoFingerTapUndoEnabled, value);

    [ObservableProperty]
    private bool threeFingerTapRedoEnabled;

    partial void OnThreeFingerTapRedoEnabledChanged(bool value) =>
        Persist(PreferenceKeys.ThreeFingerTapRedoEnabled, value);

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

    /// <summary>
    /// The way of reaching the drawing tools that is stored, as an index into <see cref="ToolUis"/>. A
    /// value that is not one of them - an empty store, or one written by a version that knew only the
    /// other way - falls back on the radial menu.
    /// </summary>
    private int ReadInkToolUiIndex()
    {
        var stored = preferences.Get(PreferenceKeys.InkToolUi, InkToolUi.RadialMenu.ToString());
        var toolUi = Enum.TryParse<InkToolUi>(stored, out var parsed) ? parsed : InkToolUi.RadialMenu;
        return Math.Max(Array.IndexOf(ToolUis, toolUi), 0);
    }

    private bool ReadBool(string key, bool @default) =>
        bool.TryParse(preferences.Get(key, @default.ToString()), out var value) ? value : @default;
}
