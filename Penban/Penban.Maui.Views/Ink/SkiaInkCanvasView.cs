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
/// <para>
/// This is the renderer that stores its coordinates in <see cref="InkDocument"/> space: the surface
/// pixels a touch arrives in are converted on the way in, and the document is scaled onto the surface
/// on the way out. A stroke therefore lands in the same place on the note and in the board preview.
/// </para>
/// </summary>
public class SkiaInkCanvasView : ContentView, IInkCanvasView
{
    /// <summary>Stroke width in document units (0.4% of the note side).</summary>
    private const float DefaultStrokeThickness = 4f;

    /// <summary>
    /// How far from the touch point a stroke is erased, in document units. Expressed in the document
    /// rather than in pixels so the eraser covers the same part of the note on every device.
    /// </summary>
    private const float EraseRadius = 14f;

    private const float HalfPi = MathF.PI / 2f;

    /// <summary>Tilt below this (about one degree) counts as "pen held upright".</summary>
    private const float MinimumTilt = 0.02f;

    /// <summary>How much wider the nib gets when the pen is laid flat against the surface.</summary>
    private const float NibWidening = 2.5f;

    /// <summary>Upper bound on nib stamps per segment, to keep long strokes cheap to redraw.</summary>
    private const int MaxNibStampsPerSegment = 12;

    private readonly SKCanvasView canvasView;
    private readonly InkCommandStack commands;

    /// <summary>Ids of the fingers currently down on the surface.</summary>
    private readonly HashSet<long> activeFingers = new();

    private InkStroke? currentStroke;
    private float penTilt;
    private float penAzimuth;

    /// <summary>Whether the stroke being drawn came from a finger rather than from a pen or mouse.</summary>
    private bool currentStrokeIsFinger;

    /// <summary>How many pens or mice are down, so a resting hand cannot interrupt one of them.</summary>
    private int activeStylusCount;

    /// <summary>
    /// True from the moment a second finger lands until the last one is lifted again. A two- or
    /// three-finger tap is a gesture (undo/redo) rather than a stroke, but the fingers reach the
    /// canvas before the gesture is recognised, so the drawing has to be held back here.
    /// </summary>
    private bool isMultiTouchGesture;

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
        commands = new InkCommandStack(Strokes);
        Content = canvasView;
    }

    public ObservableStrokeCollection Strokes { get; } = new();

    public event EventHandler? StrokeCompleted;

    public string StrokeColor { get; set; } = "#000000";

    /// <summary>Width of new strokes, in document units.</summary>
    public float StrokeThickness { get; set; } = DefaultStrokeThickness;

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

    /// <summary>
    /// Whether the pen tilt recorded in <see cref="InkPoint.Tilt"/>/<see cref="InkPoint.Azimuth"/> is
    /// turned into a calligraphy look. Has no visible effect on strokes whose tilt is zero, so a
    /// device that reports no tilt draws exactly as before.
    /// </summary>
    public bool TiltRenderingEffect { get; set; }

    /// <summary>
    /// Tilt and lean direction the stylus currently reports, fed in by the platform input handler.
    /// Both are radians; <c>0</c> means "upright" and "unknown", which is what every input device that
    /// does not report tilt leaves behind.
    /// </summary>
    public void SetStylusTilt(float tilt, float azimuth)
    {
        penTilt = tilt;
        penAzimuth = azimuth;
    }

    public void Clear()
    {
        Strokes.Clear();
        commands.Reset();
        ResetInputState();
        canvasView.InvalidateSurface();
    }

    public bool CanUndo => commands.CanUndo;

    public bool CanRedo => commands.CanRedo;

    public void Undo()
    {
        if (!commands.Undo())
        {
            return;
        }

        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!commands.Redo())
        {
            return;
        }

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

        // The strokes now in the set were never drawn here, so there is nothing meaningful to undo.
        commands.Reset();
        ResetInputState();
        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// Forgets which contacts are down. Needed whenever the stroke set is replaced underneath a
    /// possibly ongoing touch, so a half-finished stroke cannot write into a set it is no longer in.
    /// </summary>
    private void ResetInputState()
    {
        currentStroke = null;
        currentStrokeIsFinger = false;
        activeFingers.Clear();
        activeStylusCount = 0;
        isMultiTouchGesture = false;
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

        if (TrackMultiTouch(e))
        {
            e.Handled = true;
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
                currentStrokeIsFinger = e.DeviceType == SKTouchDeviceType.Touch;
                AddPoint(currentStroke, e);
                Strokes.Add(currentStroke);
                commands.RecordAdded(currentStroke);
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

    /// <summary>
    /// Follows the contacts on the surface and reports whether this touch belongs to a multi-finger
    /// gesture rather than to a stroke.
    /// <para>
    /// A two- or three-finger tap (undo/redo) is only recognised once it is over, by which time the
    /// fingers have long been reported as drawing input, so the stroke the first finger started has
    /// to be dropped here. A pen or mouse that is down at the same time wins: a resting hand must
    /// never cut a pen stroke short.
    /// </para>
    /// </summary>
    private bool TrackMultiTouch(SKTouchEventArgs e)
    {
        var isFinger = e.DeviceType == SKTouchDeviceType.Touch;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                if (isFinger)
                {
                    activeFingers.Add(e.Id);
                    if (activeFingers.Count > 1 && activeStylusCount == 0)
                    {
                        isMultiTouchGesture = true;
                        if (currentStrokeIsFinger)
                        {
                            DropCurrentStroke();
                        }
                    }
                }
                else
                {
                    activeStylusCount++;
                }

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (isFinger)
                {
                    activeFingers.Remove(e.Id);
                    if (activeFingers.Count == 0)
                    {
                        isMultiTouchGesture = false;
                    }
                }
                else
                {
                    activeStylusCount = Math.Max(activeStylusCount - 1, 0);
                }

                break;
        }

        return isMultiTouchGesture;
    }

    /// <summary>
    /// Throws the stroke that is currently being drawn away again, without a trace and without an
    /// entry in the history - for a touch that turns out not to be a stroke after all.
    /// </summary>
    private void DropCurrentStroke()
    {
        if (currentStroke is null)
        {
            return;
        }

        Strokes.Remove(currentStroke);
        commands.Discard(currentStroke);
        currentStroke = null;
        currentStrokeIsFinger = false;
        canvasView.InvalidateSurface();
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

        var touched = ToDocument(e.Location);
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
    /// <see cref="HandleEraseTouch"/>, which converts the touch to document units like any other.
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

        DropCurrentStroke();

        var erased = hasTouched && EraseStrokesAt(touched.X, touched.Y);
        canvasView.InvalidateSurface();

        if (erased)
        {
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends the eraser-end contact; the next touch writes again.</summary>
    public void EndPenTailErase() => isPenTailErasing = false;

    /// <summary>
    /// Removes every stroke that passes within <see cref="EraseRadius"/> of the point and records them
    /// with the position they had, so undo can bring them back exactly as they were.
    /// </summary>
    private bool EraseStrokesAt(float x, float y)
    {
        List<InkStrokePlacement>? removed = null;
        for (var i = Strokes.Count - 1; i >= 0; i--)
        {
            var stroke = Strokes[i];
            if (!stroke.Points.Any(p => Distance(p.X, p.Y, x, y) <= EraseRadius))
            {
                continue;
            }

            // The loop runs backwards, so removing the stroke keeps the indices of the strokes that
            // are still to be looked at - and this index is the one the stroke had before the erase.
            removed ??= new List<InkStrokePlacement>();
            removed.Add(new InkStrokePlacement(i, stroke));
            Strokes.RemoveAt(i);
        }

        if (removed is null)
        {
            return false;
        }

        removed.Reverse();
        commands.RecordErased(removed);
        return true;
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// The surface the touch coordinates arrive in. Before the first paint the surface size is not
    /// known yet, so the layout size stands in for it - a very early touch would otherwise be stored
    /// as a coordinate far outside the document.
    /// </summary>
    private SKSize SurfaceSize
    {
        get
        {
            var size = canvasView.CanvasSize;
            if (size.Width > 0 && size.Height > 0)
            {
                return size;
            }

            return new SKSize((float)Math.Max(Width, 1), (float)Math.Max(Height, 1));
        }
    }

    /// <summary>Converts a point on the surface into <see cref="InkDocument"/> units.</summary>
    private SKPoint ToDocument(SKPoint location)
    {
        var size = SurfaceSize;
        return new SKPoint(
            location.X / size.Width * InkDocument.Size,
            location.Y / size.Height * InkDocument.Size);
    }

    private void AddPoint(InkStroke stroke, SKTouchEventArgs e)
    {
        var point = ToDocument(e.Location);
        stroke.Points.Add(new InkPoint
        {
            X = point.X,
            Y = point.Y,
            Pressure = e.Pressure > 0 ? e.Pressure : 1f,
            Tilt = penTilt,
            Azimuth = penAzimuth,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        using var nibPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        // Everything below works in document units; the surface only has to scale them, and the note
        // is square so one factor is enough. This is what makes a stroke land in the same place in the
        // note editor and in the board preview.
        canvas.Save();
        canvas.Scale(info.Width / InkDocument.Size, info.Height / InkDocument.Size);

        foreach (var stroke in Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            paint.Color = SKColor.Parse(stroke.Color);
            nibPaint.Color = paint.Color;

            var first = stroke.Points[0];

            if (stroke.Points.Count == 1)
            {
                // Render a dot for a single tap - the nib itself, so a tilted pen leaves the mark its
                // shape suggests.
                if (TiltRenderingEffect && first.Tilt > MinimumTilt)
                {
                    StampNib(canvas, nibPaint, first.X, first.Y, first.Tilt, first.Azimuth, stroke.Thickness);
                }
                else
                {
                    canvas.DrawCircle(first.X, first.Y, Math.Max(stroke.Thickness / 2f, 1f), nibPaint);
                }

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
                var thickness = stroke.Thickness * (pressure > 0 ? pressure : 1f);

                if (TiltRenderingEffect && from.Tilt > MinimumTilt)
                {
                    // A pen laid flat writes with the side of its nib: the mark is as wide as the nib
                    // is long and as thin as it is thick, so the width depends on the direction the pen
                    // is dragged in - which is exactly what stamping a rotated ellipse produces.
                    StampNibSegment(canvas, nibPaint, from, to, thickness);
                    continue;
                }

                paint.StrokeWidth = thickness;
                canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// Draws one segment by stamping the pen's nib along it. The nib is an ellipse whose long axis
    /// follows the lean direction and whose length grows as the pen is laid flatter, so dragging the
    /// nib sideways leaves a broad mark and dragging it along its own long axis leaves a thin one.
    /// </summary>
    private void StampNibSegment(SKCanvas canvas, SKPaint paint, InkPoint from, InkPoint to, float thickness)
    {
        var distance = Distance(from.X, from.Y, to.X, to.Y);

        // Enough stamps that the nib footprints overlap; without that a fast stroke would come out as
        // a row of dots. The pen's lean is taken from the start of the segment - it changes too slowly
        // for interpolating it per segment to be worth the cost.
        var spacing = MathF.Max(thickness / 2f, 0.5f);
        var steps = Math.Clamp((int)MathF.Ceiling(distance / spacing), 1, MaxNibStampsPerSegment);

        for (var step = 1; step <= steps; step++)
        {
            var t = step / (float)steps;
            StampNib(
                canvas,
                paint,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                from.Tilt,
                from.Azimuth,
                thickness);
        }
    }

    /// <summary>Stamps a single nib footprint at the given point.</summary>
    private void StampNib(SKCanvas canvas, SKPaint paint, float x, float y, float tilt, float azimuth, float thickness)
    {
        var minor = MathF.Max(thickness, 0.5f) / 2f;
        var flatten = Math.Clamp(tilt / HalfPi, 0f, 1f);
        var major = minor * (1f + (NibWidening * flatten));

        // Drawn around the origin and then rotated and moved into place, so the rotation cannot shift
        // the centre of the ellipse.
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateRadians(azimuth);
        canvas.DrawOval(0f, 0f, major, minor, paint);
        canvas.Restore();
    }
}
