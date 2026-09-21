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

    /// <summary>
    /// True while the eraser end of a pen is held against the surface. Unlike
    /// <see cref="IsEraserMode"/> this is not a mode that stays on: it lasts exactly as long as the
    /// tail is down, so lifting it goes straight back to writing.
    /// </summary>
    private bool isPenTailErasing;

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

    public bool IsEraserMode { get; set; }

    /// <summary>
    /// When false, a finger only scrolls and pans; only a pen or the mouse draws. Useful when the
    /// hand rests on the screen while writing.
    /// </summary>
    public bool AllowFingerDrawing { get; set; } = true;

    /// <summary>
    /// Whether the line width follows the pressure reported per point. Off means one width for the
    /// whole stroke, which is also what a device that reports no pressure produces.
    /// </summary>
    public bool PressureSensitiveWidth { get; set; } = true;

    private const float EraseRadius = 16f;

    public void Clear()
    {
        Strokes.Clear();
        canvasView.InvalidateSurface();
    }

    public void Undo()
    {
        if (Strokes.Count == 0)
        {
            return;
        }

        Strokes.RemoveAt(Strokes.Count - 1);
        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
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
        // A finger is not a pen: with finger drawing off the touch is left unhandled so the
        // surrounding scroll/pan can have it.
        if (!AllowFingerDrawing && e.DeviceType == SKTouchDeviceType.Touch)
        {
            e.Handled = false;
            return;
        }

        if (IsEraserMode || isPenTailErasing)
        {
            HandleEraseTouch(e);
            return;
        }

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

    /// <summary>Removes whole strokes that pass under the touch point - simpler and more
    /// forgiving than pixel-level erasing, and easy to reason about for handwritten notes.</summary>
    private void HandleEraseTouch(SKTouchEventArgs e)
    {
        if (!AllowFingerDrawing && e.DeviceType == SKTouchDeviceType.Touch)
        {
            e.Handled = false;
            return;
        }

        if (e.ActionType is not (SKTouchAction.Pressed or SKTouchAction.Moved) || !e.InContact)
        {
            e.Handled = true;
            return;
        }

        var touched = e.Location;
        var erased = EraseStrokesAt(touched.X, touched.Y);

        canvasView.InvalidateSurface();
        e.Handled = true;

        if (erased)
        {
            // Reuse StrokeCompleted so callers persist the change the same way they persist
            // a freshly drawn stroke - erasing is just another edit to the stroke set.
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Flags the eraser end of the pen as being down, until <see cref="EndPenTailErase"/>. The
    /// erasing itself then needs nothing more: the flag routes every following touch to
    /// <see cref="HandleEraseTouch"/>, which already receives its coordinates in canvas pixels.
    /// </summary>
    public void BeginPenTailErase()
    {
        isPenTailErasing = true;

        if (currentStroke is null)
        {
            return;
        }

        // The platform reports the tail as an ordinary pen contact, so the press that brought it
        // down has already started a stroke by the time this is called. That stroke is dropped
        // again - the tail must not leave a mark - but its single point is where the tail touched
        // down, and erasing there is what makes a plain tap with the tail work as well as a drag.
        var hasTouched = currentStroke.Points.Count > 0;
        var touched = hasTouched ? currentStroke.Points[0] : default;

        Strokes.Remove(currentStroke);
        currentStroke = null;

        var erased = hasTouched && EraseStrokesAt(touched.X, touched.Y);
        canvasView.InvalidateSurface();

        if (erased)
        {
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends the eraser-end contact; the next touch writes again.</summary>
    public void EndPenTailErase() => isPenTailErasing = false;

    /// <summary>Removes every stroke that passes within <see cref="EraseRadius"/> of the point.</summary>
    private bool EraseStrokesAt(float x, float y)
    {
        var erased = false;
        for (var i = Strokes.Count - 1; i >= 0; i--)
        {
            var stroke = Strokes[i];
            if (stroke.Points.Any(p => Distance(p.X, p.Y, x, y) <= EraseRadius))
            {
                Strokes.RemoveAt(i);
                erased = true;
            }
        }

        return erased;
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
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

            if (!PressureSensitiveWidth)
            {
                // Fallback: one width for the whole stroke, as before the setting existed.
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
                continue;
            }

            // Pressure varies along the stroke, so the width has to as well - one path with a
            // single StrokeWidth would flatten the whole line to whatever the pen pressed last.
            // Each segment gets the mean of its two endpoints' pressure, which keeps neighbouring
            // segments close enough in width that the joins are not visible.
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var from = stroke.Points[i - 1];
                var to = stroke.Points[i];
                var pressure = (from.Pressure + to.Pressure) / 2f;

                paint.StrokeWidth = stroke.Thickness * (pressure > 0 ? pressure : 1f);
                canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
            }
        }
    }
}
