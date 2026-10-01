using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// The square in the pen flyout that turns a hue into a particular colour: white at the top left,
/// the full colour at the top right, and black along the bottom, so one finger sets how strong and
/// how bright the colour is at once. The hue itself is not on it - that is the bar above - because
/// two of the three numbers a colour needs fit a square and three do not.
/// </summary>
public sealed class ColorAreaView : GraphicsView, IDrawable
{
    private const float CornerRadius = 6f;
    private const float MarkerRadius = 6f;
    private const float MarkerThickness = 2f;

    private float hue;
    private float saturation = 1f;
    private float value = 1f;

    public ColorAreaView()
    {
        Drawable = this;
        StartInteraction += OnTouch;
        DragInteraction += OnTouch;
    }

    /// <summary>Raised while a finger is on the square, with the colour under it as <c>#RRGGBB</c>.</summary>
    public event EventHandler<string>? ColorChanged;

    /// <summary>
    /// The colour family the square is drawn for, in degrees. It comes from the bar above, and from
    /// the colour that is set - a colour picked off the palette moves the square to its own hue.
    /// </summary>
    public float Hue
    {
        get => hue;
        set
        {
            var wrapped = ((value % 360f) + 360f) % 360f;
            if (Math.Abs(wrapped - hue) < 0.01f)
            {
                return;
            }

            hue = wrapped;
            Invalidate();
        }
    }

    /// <summary>The colour the mark stands on.</summary>
    public string Color => PenColor.FromHsv(hue, saturation, value);

    /// <summary>Moves the mark onto a colour, without raising <see cref="ColorChanged"/>.</summary>
    public void SetColor(string? hex)
    {
        var (pickedHue, pickedSaturation, pickedValue) = PenColor.ToHsv(hex ?? string.Empty);

        hue = pickedHue;
        saturation = pickedSaturation;
        value = pickedValue;
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var rect = new RectF(dirtyRect.Left, dirtyRect.Top, dirtyRect.Width, dirtyRect.Height);
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var shape = new PathF();
        shape.AppendRoundedRectangle(rect, CornerRadius);

        canvas.SaveState();
        canvas.ClipPath(shape);

        // The square is the hue at full strength, then white laid over it from the left and black
        // from the bottom: where the two meet is every saturation and every brightness of that hue.
        canvas.FillColor = PenPaint.ToColor(PenColor.FromHsv(hue, 1f, 1f));
        canvas.FillRectangle(rect);

        canvas.SetFillPaint(
            new LinearGradientPaint
            {
                StartColor = Colors.White,
                EndColor = Colors.White.WithAlpha(0f),
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
            },
            rect);
        canvas.FillRectangle(rect);

        canvas.SetFillPaint(
            new LinearGradientPaint
            {
                StartColor = Colors.Black.WithAlpha(0f),
                EndColor = Colors.Black,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            },
            rect);
        canvas.FillRectangle(rect);

        canvas.RestoreState();

        var centerX = rect.Left + (saturation * rect.Width);
        var centerY = rect.Top + ((1f - value) * rect.Height);

        // Two rings rather than one, for the same reason as on the hue bar: the mark has to be visible
        // over both the white corner and the black one.
        canvas.StrokeSize = MarkerThickness + 2f;
        canvas.StrokeColor = Colors.Black.WithAlpha(0.35f);
        canvas.DrawEllipse(centerX - MarkerRadius, centerY - MarkerRadius, MarkerRadius * 2f, MarkerRadius * 2f);
        canvas.StrokeSize = MarkerThickness;
        canvas.StrokeColor = Colors.White;
        canvas.DrawEllipse(centerX - MarkerRadius, centerY - MarkerRadius, MarkerRadius * 2f, MarkerRadius * 2f);
    }

    private void OnTouch(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }

        var width = (float)Width;
        var height = (float)Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        saturation = Math.Clamp(e.Touches[0].X / width, 0f, 1f);
        value = Math.Clamp(1f - (e.Touches[0].Y / height), 0f, 1f);

        Invalidate();
        ColorChanged?.Invoke(this, Color);
    }
}
