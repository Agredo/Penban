// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
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
    /// <summary>Order of <see cref="InkToolUiNames"/>; index in this array is the picked value.</summary>
    private static readonly InkToolUi[] ToolUis =
    [
        InkToolUi.RadialMenu,
        InkToolUi.ToolBar,
    ];

    /// <summary>
    /// Order of <see cref="StartPageNames"/>; index in this array is the picked value. The routes of
    /// the areas themselves, so that what is stored is the route the shell opens on and not a copy of
    /// it that could drift away from the shell's own.
    /// </summary>
    private static readonly string[] StartPages =
    [
        AppRoutes.Dashboard,
        AppRoutes.Projects,
        AppRoutes.Boards,
    ];

    private readonly IPreferences preferences;
    private readonly ICardService cardService;
    private readonly IRecognitionQueue queue;

    /// <summary>
    /// How often the running pass asks the queue how much is left. Reading a note takes the better
    /// part of a second, so this keeps the bar moving without waking the machine for nothing.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Suppresses the write-through while <see cref="Refresh"/> is reading, so loading the
    /// page does not write back every value it just read.</summary>
    private bool isLoading;

    public SettingsViewModel(IPreferences preferences, ICardService cardService, IRecognitionQueue queue)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(cardService);
        ArgumentNullException.ThrowIfNull(queue);

        this.preferences = preferences;
        this.cardService = cardService;
        this.queue = queue;
        Refresh();
    }

    /// <summary>
    /// Display names of the two settings of <see cref="SelectedInkToolUiIndex"/>, in its order.
    /// </summary>
    public IReadOnlyList<string> InkToolUiNames { get; } = [Strings.InkToolUiRadialMenu, Strings.InkToolUiToolBar];

    /// <summary>
    /// Display names of the three areas of <see cref="SelectedStartPageIndex"/>, in its order.
    /// </summary>
    public IReadOnlyList<string> StartPageNames { get; } =
        [Strings.DashboardTitle, Strings.ProjectsTitle, Strings.BoardsTitle];

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
            SelectedStartPageIndex = ReadStartPageIndex();
            PencilDoubleTapEnabled = ReadBool(PreferenceKeys.PencilDoubleTapEnabled, true);
            PenTailEraserEnabled = ReadBool(PreferenceKeys.PenTailEraserEnabled, true);
            PenButtonLassoEnabled = ReadBool(PreferenceKeys.PenButtonLassoEnabled, true);
            PressureSensitiveWidth = ReadBool(PreferenceKeys.PressureSensitiveWidth, true);
            AutoSizeCards = ReadBool(PreferenceKeys.AutoSizeCards, false);
            TextModeDefault = CardText.ParseMode(
                preferences.Get(PreferenceKeys.CardDefaultContentMode, string.Empty)) == CardContentMode.Text;
            HideInkInTextMode = ReadBool(PreferenceKeys.TextHideInkInTextMode, true);
            TiltDetectionEnabled = ReadBool(PreferenceKeys.TiltDetectionEnabled, false);
            TiltRenderingEffect = ReadBool(PreferenceKeys.TiltRenderingEffect, false);
            ShapeRecognitionEnabled = ReadBool(PreferenceKeys.ShapeRecognitionEnabled, true);
            TwoFingerTapUndoEnabled = ReadBool(PreferenceKeys.TwoFingerTapUndoEnabled, true);
            ThreeFingerTapRedoEnabled = ReadBool(PreferenceKeys.ThreeFingerTapRedoEnabled, true);
            RecognitionEnabled = ReadBool(PreferenceKeys.RecognitionEnabled, true);
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
    /// Whether the drawing tools are also reached from the radial menu, as an index into <see
    /// cref="ToolUis"/>. The radial menu is the default, because a finger on the note is then all it
    /// takes to change tools.
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

    /// <summary>
    /// Which area the app opens on, as an index into <see cref="StartPages"/>. The dashboard is the
    /// default: it is the one of the three that says what was worked on lately, which is the question
    /// an app that was just opened is asked. The flyout reaches all three either way, so this decides
    /// nothing but where a cold start lands.
    /// </summary>
    [ObservableProperty]
    private int selectedStartPageIndex;

    partial void OnSelectedStartPageIndexChanged(int value)
    {
        if (!isLoading && value >= 0 && value < StartPages.Length)
        {
            preferences.Set(PreferenceKeys.StartPage, StartPages[value]);
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

    /// <summary>
    /// Picking strokes up with the button on the pen itself. Which button that is only Windows can
    /// say, so the settings page hides the row everywhere else. The pen button is what the eraser-end
    /// switch above is not: the tool is not changed for good but only for as long as it is held.
    /// </summary>
    [ObservableProperty]
    private bool penButtonLassoEnabled;

    partial void OnPenButtonLassoEnabledChanged(bool value) => Persist(PreferenceKeys.PenButtonLassoEnabled, value);

    [ObservableProperty]
    private bool pressureSensitiveWidth;

    partial void OnPressureSensitiveWidthChanged(bool value) => Persist(PreferenceKeys.PressureSensitiveWidth, value);

    /// <summary>
    /// Whether the pen's lean is read at all. It is not only a switch for the effect below: on its own
    /// it widens the stroke the flatter the pen is held, the way an upright pen writes thin and one
    /// laid flat writes broad.
    /// </summary>
    [ObservableProperty]
    private bool tiltDetectionEnabled;

    partial void OnTiltDetectionEnabledChanged(bool value) => Persist(PreferenceKeys.TiltDetectionEnabled, value);

    /// <summary>
    /// Depends on <see cref="TiltDetectionEnabled"/>, which is what reads the lean: this one turns the
    /// width it gives into the nib's own shape, so the width follows the direction the pen is dragged
    /// in. Without the lean there is nothing to shape, so the page disables this switch while that one
    /// is off.
    /// </summary>
    [ObservableProperty]
    private bool tiltRenderingEffect;

    partial void OnTiltRenderingEffectChanged(bool value) => Persist(PreferenceKeys.TiltRenderingEffect, value);

    /// <summary>
    /// Whether a stroke that is left standing for a moment is read as the shape it was drawn as and
    /// replaced by it. On by default: it only ever answers a stroke that was left alone for longer
    /// than writing one takes, so a note that is only written in never sees it.
    /// </summary>
    [ObservableProperty]
    private bool shapeRecognitionEnabled;

    partial void OnShapeRecognitionEnabledChanged(bool value) =>
        Persist(PreferenceKeys.ShapeRecognitionEnabled, value);

    [ObservableProperty]
    private bool autoSizeCards;

    partial void OnAutoSizeCardsChanged(bool value) => Persist(PreferenceKeys.AutoSizeCards, value);

    /// <summary>
    /// Whether a new, empty note starts in the text field rather than under the pen. Only a start:
    /// a note that already carries writing opens in the mode it was left in, because a setting that
    /// reached back into existing notes would hide what is on them.
    /// </summary>
    [ObservableProperty]
    private bool textModeDefault;

    partial void OnTextModeDefaultChanged(bool value) => Persist(
        PreferenceKeys.CardDefaultContentMode,
        CardText.FormatMode(value ? CardContentMode.Text : CardContentMode.Ink));

    /// <summary>
    /// Whether the ink steps back while a note is written on the keyboard. On by default: ink under
    /// the field is what makes a note hard to read while it is being typed into, and whoever wants
    /// it as a background can say so here.
    /// </summary>
    [ObservableProperty]
    private bool hideInkInTextMode;

    partial void OnHideInkInTextModeChanged(bool value) => Persist(PreferenceKeys.TextHideInkInTextMode, value);

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

    /// <summary>
    /// Whether the handwriting of notes is read in the background, which is what makes their text
    /// findable. On by default, and worth a switch of its own because reading a board costs real
    /// time on the processor - the one setting here whose cost is measured in minutes.
    /// </summary>
    [ObservableProperty]
    private bool recognitionEnabled;

    partial void OnRecognitionEnabledChanged(bool value) => Persist(PreferenceKeys.RecognitionEnabled, value);

    /// <summary>
    /// Whether the one-off pass over the notes that were written before the switch was turned on is
    /// running. While it runs the button is out of reach and the bar under it is shown.
    /// </summary>
    [ObservableProperty]
    private bool isIndexingNotes;

    /// <summary>
    /// Whether a pass has finished, which is when the line under the button says how many notes came
    /// out of it. Kept apart from <see cref="IsIndexingNotes"/> so the two never show at once.
    /// </summary>
    [ObservableProperty]
    private bool hasIndexedNotes;

    /// <summary>How many notes the running pass was handed, and how many of them are done.</summary>
    [ObservableProperty]
    private int indexTotal;

    [ObservableProperty]
    private int indexDone;

    /// <summary>How far the pass has come, for the bar. Nothing runs, nothing to show.</summary>
    public double IndexProgress => IndexTotal <= 0 ? 0 : Math.Clamp((double)IndexDone / IndexTotal, 0, 1);

    /// <summary>How far the pass has come, as the line under the bar says it.</summary>
    public string IndexProgressLabel =>
        string.Format(Strings.RecognitionIndexProgressFormat, IndexDone, IndexTotal);

    /// <summary>What the pass reports once it is over.</summary>
    public string IndexResultLabel => string.Format(Strings.RecognitionIndexDoneFormat, IndexDone);

    private bool CanIndexNotes => !IsIndexingNotes;

    partial void OnIsIndexingNotesChanged(bool value) => IndexAllNotesCommand.NotifyCanExecuteChanged();

    partial void OnIndexTotalChanged(int value)
    {
        OnPropertyChanged(nameof(IndexProgress));
        OnPropertyChanged(nameof(IndexProgressLabel));
        OnPropertyChanged(nameof(IndexResultLabel));
    }

    partial void OnIndexDoneChanged(int value)
    {
        OnPropertyChanged(nameof(IndexProgress));
        OnPropertyChanged(nameof(IndexProgressLabel));
        OnPropertyChanged(nameof(IndexResultLabel));
    }

    /// <summary>
    /// How many notes the queue has finished since the pass started. Written on the queue's worker
    /// thread and read on the caller's, hence a counter and not a plain field - and hence not
    /// <see cref="IndexDone"/>, which may only be written from the thread that owns the bindings.
    /// </summary>
    private int notesRead;

    /// <summary>
    /// Reads every note in the app once. Reading happens when a note is saved or when its board is
    /// opened, so the notes that were already there when the switch was turned on would otherwise
    /// only become findable board by board - and a board nobody opens again never at all.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanIndexNotes))]
    private async Task IndexAllNotesAsync()
    {
        HasIndexedNotes = false;
        IndexTotal = 0;
        IndexDone = 0;
        IsIndexingNotes = true;

        void Count(object? sender, Guid cardId) => Interlocked.Increment(ref notesRead);
        void CountFailure(object? sender, RecognitionFailure failure) => Interlocked.Increment(ref notesRead);

        notesRead = 0;
        queue.Recognized += Count;
        queue.Failed += CountFailure;

        try
        {
            // Nothing is handed over while the switch is off, so asking for the count anyway would
            // count notes the queue never got.
            IndexTotal = RecognitionEnabled ? await cardService.QueueAllRecognitionAsync() : 0;

            // The queue reads on its own worker and reports nothing the caller waits for, so the pass
            // follows the queue's own count of finished notes rather than waiting for an answer per
            // note. Working it out from what is still waiting instead would be wrong: turning the
            // switch off makes the queue throw a waiting note away, and a thrown-away note leaves the
            // queue looking exactly like a read one.
            while (IndexTotal > 0)
            {
                // Clamped because the queue also reads notes saved while the pass runs; that can only
                // push the count past the total, never the bar past full.
                IndexDone = Math.Min(Volatile.Read(ref notesRead), IndexTotal);

                if (queue.PendingCount == 0 && !queue.IsReading)
                {
                    break;
                }

                await Task.Delay(PollInterval);
            }

            HasIndexedNotes = true;
        }
        finally
        {
            queue.Recognized -= Count;
            queue.Failed -= CountFailure;
            IsIndexingNotes = false;
        }
    }

    private void Persist(string key, bool value) => Persist(key, value.ToString());

    private void Persist(string key, string value)
    {
        if (!isLoading)
        {
            preferences.Set(key, value);
        }
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

    /// <summary>
    /// The area the app was last told to open on. Anything that is not one of the three - a store that
    /// has never held this setting, or one that holds a route that no longer exists - means the
    /// dashboard, so a start page that cannot be honoured falls back rather than leaving the app with
    /// nowhere to land.
    /// </summary>
    private int ReadStartPageIndex()
    {
        var stored = preferences.Get(PreferenceKeys.StartPage, AppRoutes.Dashboard);
        return Math.Max(Array.IndexOf(StartPages, stored), 0);
    }

    private bool ReadBool(string key, bool @default) =>
        bool.TryParse(preferences.Get(key, @default.ToString()), out var value) ? value : @default;
}
