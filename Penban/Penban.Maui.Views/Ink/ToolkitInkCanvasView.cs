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
/// </summary>
public class ToolkitInkCanvasView : ContentView, IInkCanvasView
{
    private readonly DrawingView drawingView;

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
    }

    public void Undo()
    {
        if (drawingView.Lines.Count > 0)
        {
            drawingView.Lines.RemoveAt(drawingView.Lines.Count - 1);
        }

        if (Strokes.Count > 0)
        {
            Strokes.RemoveAt(Strokes.Count - 1);
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
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
    }

    private void OnDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
    {
        var stroke = new InkStroke
        {
            Color = ToHex(e.LastDrawingLine.LineColor),
            Thickness = e.LastDrawingLine.LineWidth,
        };

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var point in e.LastDrawingLine.Points)
        {
            stroke.Points.Add(new InkPoint
            {
                X = point.X,
                Y = point.Y,
                Pressure = 1f,
                TimestampMs = timestamp,
            });
        }

        Strokes.Add(stroke);
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static string ToHex(Color color) =>
        $"#{(byte)(color.Red * 255):X2}{(byte)(color.Green * 255):X2}{(byte)(color.Blue * 255):X2}";
}
