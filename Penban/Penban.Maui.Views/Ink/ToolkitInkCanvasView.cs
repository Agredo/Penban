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
/// Limitation: <see cref="IsEraserMode"/> is exposed for interface parity with
/// <see cref="SkiaInkCanvasView"/>, but <see cref="DrawingView"/> does not expose a raw touch
/// hook this renderer can use to hit-test strokes while drawing, so setting it here has no
/// effect. <see cref="Undo"/> works normally on both renderers.
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

    public bool IsEraserMode { get; set; }

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
