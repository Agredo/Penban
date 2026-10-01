using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// The bar of hues in the pen flyout: the spectrum from red round to red again, with a mark at the
/// hue that is set. It answers the one question a finger can answer along a line - which colour
/// family - and hands the rest to the square beside it, which is where a colour becomes a particular
/// one. Drawn rather than built from a package: it is a rectangle of colours and a mark on it.
/// </summary>
public sealed class HueStripView : GraphicsView, IDrawable
{
    /// <summary>Bands the spectrum is drawn in. Enough that the joins are not visible at this size.</summary>
    private const int Bands = 60;

    /// <summary>Room kept above and below the bar for the mark to stand in.</summary>
    private const float MarkerInset = 3f;

    private const float MarkerRadius = 5f;
    private const float MarkerThickness = 2f;
    private const float BarRadius = 5f;

    private float hue;

    public HueStripView()
    {
        Drawable = this;
        StartInteraction += OnTouch;
        DragInteraction += OnTouch;
    }

    /// <summary>Raised while a finger is on the bar, with the hue under it in degrees.</summary>
    public event EventHandler<float>? HueChanged;

    /// <summary>
    /// The hue the mark stands at, in degrees. Set from the colour that is set, so the mark is where
    /// that colour's hue is; moving the finger here sets the colour instead.
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

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var bar = BarRect(dirtyRect);
        if (bar.Width <= 0f || bar.Height <= 0f)
        {
            return;
        }

        // The bands are clipped to the bar's own rounded shape, so the spectrum has the same corner as
        // everything else in the flyout instead of square corners poking out of a rounded outline.
        canvas.SaveState();
        var outline = new PathF();
        outline.AppendRoundedRectangle(bar, BarRadius);
        canvas.ClipPath(outline);

        var band = bar.Width / Bands;
        for (var index = 0; index < Bands; index++)
        {
            // Drawn one band wider than it is, so rounding cannot leave a hairline of background
            // between two of them.
            canvas.FillColor = PenPaint.ToColor(PenColor.FromHsv(index * 360f / Bands, 1f, 1f));
            canvas.FillRectangle(bar.Left + (index * band), bar.Top, band + 1f, bar.Height);
        }

        canvas.RestoreState();

        var centerX = bar.Left + (hue / 360f * bar.Width);
        var centerY = bar.Center.Y;

        // Two rings rather than one: the bar runs through every brightness, so a mark in a single
        // colour would vanish over part of it.
        canvas.StrokeSize = MarkerThickness + 2f;
        canvas.StrokeColor = Colors.Black.WithAlpha(0.35f);
        canvas.DrawEllipse(centerX - MarkerRadius, centerY - MarkerRadius, MarkerRadius * 2f, MarkerRadius * 2f);
        canvas.StrokeSize = MarkerThickness;
        canvas.StrokeColor = Colors.White;
        canvas.DrawEllipse(centerX - MarkerRadius, centerY - MarkerRadius, MarkerRadius * 2f, MarkerRadius * 2f);
    }

    private static RectF BarRect(RectF dirtyRect) =>
        new(dirtyRect.Left, dirtyRect.Top + MarkerInset, dirtyRect.Width, dirtyRect.Height - (MarkerInset * 2f));

    private void OnTouch(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }

        var bar = BarRect(new RectF(0f, 0f, (float)Width, (float)Height));
        if (bar.Width <= 0f)
        {
            return;
        }

        var share = Math.Clamp((e.Touches[0].X - bar.Left) / bar.Width, 0f, 1f);
        var picked = share * 360f;

        hue = picked >= 360f ? 359f : picked;
        Invalidate();
        HueChanged?.Invoke(this, hue);
    }
}
