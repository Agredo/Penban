using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// One pen of the editor's row, drawn as the thing itself instead of as a button labelled with a
/// colour: a cap in the colour the pen writes in, a blade whose width follows how wide the pen
/// writes, and a tip in the same colour. Both come from the pen slot the glyph stands for, so the
/// row shows what the pen will do rather than naming it.
/// </summary>
public sealed class PenGlyphView : GraphicsView, IDrawable
{
    /// <summary>
    /// The pen is drawn in this box and then scaled into whatever the view is given, so the shape is
    /// written down once in its own proportions and holds at any size. It is taller than it is wide,
    /// which is the one thing about the pen that must not follow the width it writes at - a wider pen
    /// is a thicker pen, not a longer one.
    /// </summary>
    private const float ArtWidth = 30f;

    private const float ArtHeight = 64f;

    /// <summary>Where the cap, the blade and the tip meet, from the top of the drawing down.</summary>
    private const float CapTop = 4f;

    private const float CapBottom = 25f;
    private const float BladeBottom = 52f;
    private const float TipBottom = 60f;

    /// <summary>Blade width at the thinnest and the widest pen, and how much is added in between.</summary>
    private const float BladeWidth = 8.5f;

    private const float BladeWidthGain = 5.5f;

    /// <summary>The cap is wider than the blade and grows far more slowly, the way a real pen's does.</summary>
    private const float CapWidth = 13f;

    private const float CapWidthGain = 3f;

    /// <summary>The tip narrows to this share of the blade, so a wide pen has a broad point.</summary>
    private const float TipWidthShare = 0.5f;

    private const float OutlineThickness = 1.5f;
    private const float CornerRadius = 1.5f;

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(Color),
        typeof(PenGlyphView),
        Colors.Black,
        propertyChanged: (bindable, _, _) => ((PenGlyphView)bindable).Invalidate());

    public static readonly BindableProperty ThicknessProperty = BindableProperty.Create(
        nameof(Thickness),
        typeof(float),
        typeof(PenGlyphView),
        PenThickness.Default,
        propertyChanged: (bindable, _, _) => ((PenGlyphView)bindable).Invalidate());

    /// <summary>
    /// The line around the pen. It cannot be a fixed colour: the row is drawn on the page's own
    /// background, which is light in one theme and dark in the other, and a black outline would
    /// disappear into the second.
    /// </summary>
    public static readonly BindableProperty OutlineColorProperty = BindableProperty.Create(
        nameof(OutlineColor),
        typeof(Color),
        typeof(PenGlyphView),
        Colors.Gray,
        propertyChanged: (bindable, _, _) => ((PenGlyphView)bindable).Invalidate());

    public PenGlyphView()
    {
        Drawable = this;
    }

    /// <summary>The colour the pen writes in, which is the colour of its cap and its tip.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// How wide the pen writes, in document units - the same width the strokes are drawn at. Only the
    /// blade follows it, and it follows it from the drawing rather than from the slot: a pen set to
    /// its widest is drawn at its widest as soon as the slider moves, without the slot being written.
    /// </summary>
    public float Thickness
    {
        get => (float)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>Colour of the outline around the pen.</summary>
    public Color OutlineColor
    {
        get => (Color)GetValue(OutlineColorProperty);
        set => SetValue(OutlineColorProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0f || dirtyRect.Height <= 0f)
        {
            return;
        }

        var share = PenThickness.Clamp(Thickness) / PenThickness.Maximum;
        var blade = BladeWidth + (share * BladeWidthGain);
        var cap = CapWidth + (share * CapWidthGain);
        var tip = blade * TipWidthShare;
        var center = ArtWidth / 2f;

        var capPath = new PathF();
        capPath.AppendRoundedRectangle(
            new RectF(center - (cap / 2f), CapTop, cap, CapBottom - CapTop),
            CornerRadius);

        // The blade starts one unit inside the cap, so the two fills meet without a hairline of
        // background showing between them.
        var bladePath = new PathF();
        bladePath.AppendRectangle(new RectF(center - (blade / 2f), CapBottom - 1f, blade, BladeBottom - CapBottom + 1f));

        var tipPath = new PathF();
        tipPath.MoveTo(center, TipBottom);
        tipPath.LineTo(center - (tip / 2f), BladeBottom);
        tipPath.LineTo(center + (tip / 2f), BladeBottom);
        tipPath.Close();

        // Fitted rather than stretched: the pen keeps its proportions whatever box it is given.
        var scale = Math.Min(dirtyRect.Width / ArtWidth, dirtyRect.Height / ArtHeight);

        canvas.SaveState();
        canvas.Translate(
            dirtyRect.Left + ((dirtyRect.Width - (ArtWidth * scale)) / 2f),
            dirtyRect.Top + ((dirtyRect.Height - (ArtHeight * scale)) / 2f));
        canvas.Scale(scale, scale);

        // Every fill first, then every outline: an outline drawn between two of them would show
        // through the fill that covers it, and the pen would come out looking cut into pieces.
        canvas.FillColor = Color;
        canvas.FillPath(capPath);
        canvas.FillColor = Colors.White;
        canvas.FillPath(bladePath);
        canvas.FillColor = Color;
        canvas.FillPath(tipPath);

        canvas.StrokeColor = OutlineColor;
        canvas.StrokeSize = OutlineThickness;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(capPath);
        canvas.DrawPath(bladePath);
        canvas.DrawPath(tipPath);

        canvas.RestoreState();
    }
}
