using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Penban.Models;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Ink renderer that wraps <see cref="DrawingView"/> from CommunityToolkit.Maui.
/// <para>
/// Limitation (documented per spec request): <see cref="IDrawingLine.Points"/> only exposes
/// plain <c>PointF</c> coordinates with no pressure channel, so pressure is always recorded
/// as <c>1.0</c> for strokes drawn with this renderer. Use <see cref="SkiaInkCanvasView"/>
/// if true stylus-pressure fidelity is required.
/// </para>
/// <para>
/// Limitation: <see cref="IsEraserMode"/> and <see cref="Tool"/> are exposed for interface parity with
/// <see cref="SkiaInkCanvasView"/>, but <see cref="DrawingView"/> does not expose a raw touch
/// hook this renderer can use to hit-test strokes while drawing, so setting them here has no
/// effect. <see cref="Undo"/> works normally on both renderers.
/// </para>
/// <para>
/// Limitation: <see cref="SelectionCount"/> and <see cref="DeleteSelection"/> are likewise exposed for
/// parity only. Picking strokes up off the note means reading the surface while it is being touched,
/// which this renderer cannot do - see the remarks on <see cref="Tool"/>. Use
/// <see cref="SkiaInkCanvasView"/> if picking strokes up matters.
/// </para>
/// <para>
/// Limitation: <see cref="AllowFingerDrawing"/> is likewise exposed for parity only.
/// <see cref="DrawingView"/> reports completed lines without the input device that produced
/// them, so a finger cannot be told apart from a pen and always draws. Use
/// <see cref="SkiaInkCanvasView"/> if that matters.
/// </para>
/// <para>
/// Limitation: <see cref="DrawingView"/> hands out the points of a line in the device-independent
/// units of its own surface. Those are converted to <see cref="InkDocument"/> units on the way in, so
/// the stored data is the same whichever renderer captured it, but nothing is re-rendered onto the
/// toolkit surface - see <see cref="LoadStrokes"/> and <see cref="Redo"/>.
/// </para>
/// </summary>
public class ToolkitInkCanvasView : ContentView, IInkCanvasView
{
    private readonly DrawingView drawingView;
    private readonly List<InkStroke> redoable = new();

    public ToolkitInkCanvasView()
    {
        drawingView = new DrawingView
        {
            IsMultiLineModeEnabled = true,
            ShouldClearOnFinish = false,
        };
        drawingView.DrawingLineCompleted += OnDrawingLineCompleted;
        Content = drawingView;
    }

    public ObservableStrokeCollection Strokes { get; } = new();

    public event EventHandler? StrokeCompleted;

    public event EventHandler? ToolChanged;

    /// <summary>
    /// See the class remarks: nothing is ever picked up here, so this event has nothing to raise and
    /// it is left unwired rather than kept in a field that would never fire.
    /// </summary>
    public event EventHandler? SelectionChanged
    {
        add { }
        remove { }
    }

    private InkTool tool = InkTool.Pen;

    /// <summary>
    /// Which tool the note is being worked on with. See the class remarks: <see cref="InkTool.Lasso"/>
    /// is taken and held - the row above shows it as the tool in use - but this renderer cannot read
    /// the surface while it is being touched, so nothing is ever picked up by it.
    /// </summary>
    public InkTool Tool
    {
        get => tool;
        set
        {
            if (tool == value)
            {
                return;
            }

            tool = value;
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>See the class remarks: this is <see cref="Tool"/> seen as the eraser being on or off.</summary>
    public bool IsEraserMode
    {
        get => Tool == InkTool.Eraser;
        set => Tool = value ? InkTool.Eraser : InkTool.Pen;
    }

    /// <summary>See the class remarks: this renderer never picks anything up.</summary>
    public int SelectionCount => 0;

    /// <summary>See the class remarks: this renderer never picks anything up, so there is never
    /// anything to throw away.</summary>
    public bool DeleteSelection() => false;

    /// <summary>See the class remarks: this renderer never picks anything up, so there is never a
    /// group to put down.</summary>
    public void ClearSelection()
    {
    }

    /// <summary>See the class remarks: this renderer never draws a loop, so there is never one to read
    /// before the contact that drew it has been lifted.</summary>
    public void FinishLasso()
    {
    }

    /// <summary>See the class remarks: <see cref="DrawingView"/> cannot tell input devices apart.</summary>
    public bool AllowFingerDrawing { get; set; } = true;

    public string StrokeColor
    {
        get => ToHex(drawingView.LineColor);
        set => drawingView.LineColor = Color.FromArgb(value);
    }

    public float StrokeThickness
    {
        get => drawingView.LineWidth;
        set => drawingView.LineWidth = value;
    }

    public void Clear()
    {
        drawingView.Lines.Clear();
        Strokes.Clear();
        redoable.Clear();
    }

    public bool CanUndo => Strokes.Count > 0;

    public bool CanRedo => redoable.Count > 0;

    /// <summary>
    /// See the class remarks: <see cref="DrawingView"/> cannot put a line back on its surface that it
    /// did not capture itself, so this renderer clears the surface and keeps the strokes for
    /// <see cref="Redo"/> in the order they were drawn in.
    /// </summary>
    public bool EraseAll()
    {
        if (Strokes.Count == 0)
        {
            return false;
        }

        // Reversed, because Redo takes from the end of the list: the first stroke drawn is then also
        // the first one to come back.
        for (var index = Strokes.Count - 1; index >= 0; index--)
        {
            redoable.Add(Strokes[index]);
        }

        Strokes.Clear();
        drawingView.Lines.Clear();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Undo()
    {
        if (drawingView.Lines.Count > 0)
        {
            drawingView.Lines.RemoveAt(drawingView.Lines.Count - 1);
        }

        if (Strokes.Count > 0)
        {
            var last = Strokes.Count - 1;
            redoable.Add(Strokes[last]);
            Strokes.RemoveAt(last);
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Puts the most recently undone stroke back into the stroke set. See the class remarks: it is
    /// restored in the data, but <see cref="DrawingView"/> cannot be asked to draw a line it did not
    /// capture itself, so it does not reappear on the toolkit surface.
    /// </summary>
    public void Redo()
    {
        if (redoable.Count == 0)
        {
            return;
        }

        var last = redoable.Count - 1;
        Strokes.Add(redoable[last]);
        redoable.RemoveAt(last);
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void LoadStrokes(IEnumerable<InkStroke> strokes)
    {
        // DrawingView's Lines collection is read-back only for lines it has itself drawn;
        // pre-existing strokes are kept in our own collection for persistence and simply
        // not re-rendered onto the toolkit surface (a known asymmetry of this renderer).
        Strokes.Clear();
        foreach (var stroke in strokes)
        {
            Strokes.Add(stroke);
        }

        redoable.Clear();
    }

    private void OnDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        var stroke = new InkStroke
        {
            Color = ToHex(e.LastDrawingLine.LineColor),
            Thickness = ToDocumentLength(e.LastDrawingLine.LineWidth),
            // This surface takes no pressure from the platform at all - every point below is recorded at
            // full pressure - so the stroke says so instead of letting a later renderer guess a width
            // from numbers that were never measured.
            PressureSensitiveWidth = false,
        };

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var point in e.LastDrawingLine.Points)
        {
            stroke.Points.Add(new InkPoint
            {
                X = ToDocumentLength(point.X),
                Y = ToDocumentLength(point.Y),
                Pressure = 1f,
                TimestampMs = timestamp,
            });
        }

        // A new stroke makes the undone branch unreachable.
        redoable.Clear();
        Strokes.Add(stroke);
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Converts a length on the toolkit surface into <see cref="InkDocument"/> units.</summary>
    private float ToDocumentLength(float value) =>
        value / (float)Math.Max(drawingView.Width, 1) * InkDocument.Size;

    private static string ToHex(Color color) =>
        $"#{(byte)(color.Red * 255):X2}{(byte)(color.Green * 255):X2}{(byte)(color.Blue * 255):X2}";
}
