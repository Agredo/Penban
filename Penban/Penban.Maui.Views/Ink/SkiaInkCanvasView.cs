using Penban.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Ink renderer backed by a raw <see cref="SKCanvasView"/>. Handles pointer/pen/touch
/// input manually via <see cref="SKCanvasView.Touch"/> so stylus pressure (reported through
/// <see cref="SKTouchEventArgs.Pressure"/> on platforms that support it) is preserved in the
/// stored <see cref="InkStroke"/> data.
/// </summary>
public class SkiaInkCanvasView : ContentView, IInkCanvasView
{
    private readonly SKCanvasView canvasView;
    private InkStroke? currentStroke;

    public SkiaInkCanvasView()
    {
        canvasView = new SKCanvasView { EnableTouchEvents = true };
        canvasView.Touch += OnTouch;
        canvasView.PaintSurface += OnPaintSurface;
        Content = canvasView;
    }

    public ObservableStrokeCollection Strokes { get; } = new();

    public event EventHandler? StrokeCompleted;

    public string StrokeColor { get; set; } = "#000000";

    public float StrokeThickness { get; set; } = 4f;

    public void Clear()
    {
        Strokes.Clear();
        canvasView.InvalidateSurface();
    }

    public void LoadStrokes(IEnumerable<InkStroke> strokes)
    {
        Strokes.Clear();
        foreach (var stroke in strokes)
        {
            Strokes.Add(stroke);
        }

        canvasView.InvalidateSurface();
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                currentStroke = new InkStroke { Color = StrokeColor, Thickness = StrokeThickness };
                AddPoint(currentStroke, e);
                Strokes.Add(currentStroke);
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                if (currentStroke is not null && e.InContact)
                {
                    AddPoint(currentStroke, e);
                    canvasView.InvalidateSurface();
                }

                e.Handled = true;
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (currentStroke is not null)
                {
                    currentStroke = null;
                    canvasView.InvalidateSurface();
                    StrokeCompleted?.Invoke(this, EventArgs.Empty);
                }

                e.Handled = true;
                break;
        }
    }

    private static void AddPoint(InkStroke stroke, SKTouchEventArgs e)
    {
        stroke.Points.Add(new InkPoint
        {
            X = e.Location.X,
            Y = e.Location.Y,
            Pressure = e.Pressure > 0 ? e.Pressure : 1f,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        foreach (var stroke in Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            paint.Color = SKColor.Parse(stroke.Color);

            var first = stroke.Points[0];

            if (stroke.Points.Count == 1)
            {
                // Render a dot for a single tap.
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(first.X, first.Y, Math.Max(stroke.Thickness / 2f, 1f), paint);
                paint.Style = SKPaintStyle.Stroke;
                continue;
            }

            using var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(new SKPoint(first.X, first.Y));
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var point = stroke.Points[i];
                pathBuilder.LineTo(new SKPoint(point.X, point.Y));
            }

            using var path = pathBuilder.Detach();

            paint.StrokeWidth = stroke.Thickness * (stroke.Points[^1].Pressure > 0 ? stroke.Points[^1].Pressure : 1f);
            canvas.DrawPath(path, paint);
        }
    }
}
