using System.Globalization;
using Microsoft.Maui.Graphics;
using Penban.Maui.Views.Controls;
using GraphicsFont = Microsoft.Maui.Graphics.Font;

namespace Penban.Maui.Views.Widget;

/// <summary>The three widget sizes, which differ in what they have room for.</summary>
public enum WidgetCardKind
{
    /// <summary>The square widget: the stack of notes, and nothing else.</summary>
    Square,

    /// <summary>The wide widget: stack beside title, caption and load bar.</summary>
    Wide,

    /// <summary>The tall widget: a large stack above title, caption and load bar.</summary>
    Tall,
}

/// <summary>
/// Draws one board as the home screen widget shows it: the card of the overview without its three
/// buttons - share, rename, delete make no sense on a home screen - and with the thumbnail, the title,
/// the caption and the load bar the overview puts on it.
/// <para>
/// The widget itself only shows the image this produces, which is why the drawing has to be able to
/// stand alone: no views, no bindings, no theme lookup. Everything it needs comes in through the
/// constructor.
/// </para>
/// </summary>
public sealed class WidgetCardDrawable : IDrawable
{
    /// <summary>Corner radius of the card, as in the <c>CardBorder</c> style.</summary>
    public const float CornerRadius = 12f;

    /// <summary>Room around the card for its shadow; the widget's own edge stays background.</summary>
    public const float CardInset = 8f;

    /// <summary>Room between the card's edge and its content, as on the overview.</summary>
    public const float CardPadding = 14f;

    /// <summary>Room around the notes in the square widget, whose whole surface is the card.</summary>
    public const float SquareInset = 12f;

    /// <summary>Narrowest a lane chip is drawn; below this the number on it stops reading.</summary>
    public const float MinimumChipWidth = 30f;

    /// <summary>Room between two lane chips.</summary>
    public const float ChipGap = 3f;

    /// <summary>Height of a lane chip.</summary>
    public const float ChipHeight = 22f;

    /// <summary>Height of the board title on the wide widget; two lines at 16 points.</summary>
    private const float TitleHeight = 40f;

    /// <summary>Height of the board title on the tall widget; two lines at 20 points.</summary>
    private const float TallTitleHeight = 56f;

    /// <summary>Height reserved for the caption; three lines at 12 points.</summary>
    private const float SummaryHeight = 44f;

    /// <summary>Height of the thumbnail on the tall widget.</summary>
    private const float TallStackHeight = 180f;

    /// <summary>Width of the thumbnail on the wide widget.</summary>
    private const float WideStackWidth = 150f;

    /// <summary>Room between the thumbnail and the text beside it.</summary>
    private const float StackGap = 16f;

    private const float ChipCornerRadius = 6f;

    private const float TitleSize = 16f;

    private const float TallTitleSize = 20f;

    private const float CaptionSize = 12f;

    private const float ChipSize = 12f;

    /// <summary>
    /// The app's own faces, by their real family name rather than the alias the XAML uses: the export
    /// goes through SkiaSharp, which resolves a font by family name against the fonts the platform
    /// knows - and on iOS those are the bundled files. A name it cannot resolve falls back to the
    /// system font, which keeps the widget readable but no longer matches the app.
    /// </summary>
    private const string RegularFont = "Open Sans";

    /// <summary>Open Sans SemiBold ships as a family of its own, with a Regular subfamily.</summary>
    private const string SemiboldFont = "Open Sans SemiBold";

    private const int RegularWeight = 400;

    private const int SemiboldWeight = 400;

    private readonly WidgetBoard board;
    private readonly WidgetPalette palette;
    private readonly WidgetCardKind kind;
    private readonly IReadOnlyList<NoteStackItem> notes;

    public WidgetCardDrawable(WidgetBoard board, WidgetPalette palette, WidgetCardKind kind)
    {
        this.board = board;
        this.palette = palette;
        this.kind = kind;
        notes = [.. board.Notes.Select(note => note.Item)];
    }

    /// <summary>
    /// Logical size the widget is drawn in. It is rendered at a multiple of this, so the numbers here
    /// only have to keep the proportions of the home screen: a square, a wide and a tall widget.
    /// </summary>
    public static SizeF SizeOf(WidgetCardKind kind) => kind switch
    {
        WidgetCardKind.Square => new SizeF(170f, 170f),
        WidgetCardKind.Wide => new SizeF(360f, 168f),
        _ => new SizeF(360f, 380f),
    };

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var design = SizeOf(kind);
        var area = new RectF(
            0f,
            0f,
            dirtyRect.Width > 0f ? dirtyRect.Width : design.Width,
            dirtyRect.Height > 0f ? dirtyRect.Height : design.Height);

        var scale = Math.Min(area.Width / design.Width, area.Height / design.Height);
        if (scale <= 0f)
        {
            return;
        }

        // The drawing is in the design's own units and scales itself into the rect it is given, so the
        // same code paints a preview of 170 points and the exported image of 510 pixels.
        canvas.SaveState();
        canvas.Scale(scale, scale);
        canvas.Translate((area.Width / scale - design.Width) / 2f, (area.Height / scale - design.Height) / 2f);

        var frame = new RectF(0f, 0f, design.Width, design.Height);

        if (kind == WidgetCardKind.Square)
        {
            // The square widget has no room for a word of text, so there is nothing for a card to
            // separate: the whole surface is the card, which gives the notes the room instead of
            // shrinking them into a thumbnail.
            canvas.FillColor = palette.Surface;
            canvas.FillRectangle(frame);
            new NoteStackDrawable(notes).Draw(canvas, Inset(frame, SquareInset));
        }
        else
        {
            canvas.FillColor = palette.Background;
            canvas.FillRectangle(frame);

            var card = Inset(frame, CardInset);
            DrawCard(canvas, card);

            var content = Inset(card, CardPadding);
            if (kind == WidgetCardKind.Wide)
            {
                DrawWide(canvas, content);
            }
            else
            {
                DrawTall(canvas, content);
            }
        }

        canvas.RestoreState();
    }

    private static RectF Inset(RectF rect, float amount)
        => new(rect.X + amount, rect.Y + amount, rect.Width - (amount * 2f), rect.Height - (amount * 2f));

    private void DrawCard(ICanvas canvas, RectF card)
    {
        // The shadow goes on in its own state: it would otherwise be painted under everything that
        // follows on the card as well.
        canvas.SaveState();
        canvas.SetShadow(new SizeF(0f, 2f), 8f, Color.FromRgba(0f, 0f, 0f, palette.ShadowAlpha));
        canvas.FillColor = palette.Surface;
        canvas.FillRoundedRectangle(card, CornerRadius);
        canvas.RestoreState();

        canvas.StrokeColor = palette.Border;
        canvas.StrokeSize = 1f;
        canvas.DrawRoundedRectangle(card, CornerRadius);
    }

    private void DrawWide(ICanvas canvas, RectF content)
    {
        new NoteStackDrawable(notes).Draw(canvas, new RectF(content.X, content.Y, WideStackWidth, content.Height));

        var text = new RectF(content.X + WideStackWidth + StackGap, content.Y, content.Width - WideStackWidth - StackGap, content.Height);
        DrawText(canvas, board.Title, new RectF(text.X, text.Y, text.Width, TitleHeight), TitleSize, semibold: true, palette.TextPrimary);
        DrawText(canvas, board.Summary, new RectF(text.X, text.Y + TitleHeight + 4f, text.Width, SummaryHeight), CaptionSize, semibold: false, palette.TextSecondary);
        DrawChips(canvas, new RectF(text.X, text.Bottom - ChipHeight, text.Width, ChipHeight));
    }

    private void DrawTall(ICanvas canvas, RectF content)
    {
        new NoteStackDrawable(notes).Draw(canvas, new RectF(content.X, content.Y, content.Width, TallStackHeight));

        var titleY = content.Y + TallStackHeight + 18f;
        DrawText(canvas, board.Title, new RectF(content.X, titleY, content.Width, TallTitleHeight), TallTitleSize, semibold: true, palette.TextPrimary);
        DrawText(canvas, board.Summary, new RectF(content.X, titleY + TallTitleHeight + 6f, content.Width, SummaryHeight), CaptionSize, semibold: false, palette.TextSecondary);
        DrawChips(canvas, new RectF(content.X, content.Bottom - ChipHeight, content.Width, ChipHeight));
    }

    /// <summary>
    /// Draws the load bar: one chip per lane, as wide as the lane's share of the board, so the widget
    /// answers "where is the work piled up?" without opening anything.
    /// </summary>
    private void DrawChips(ICanvas canvas, RectF area)
    {
        var columns = board.Columns;
        var available = area.Width - (ChipGap * (columns.Count - 1));
        if (columns.Count == 0 || available <= 0f)
        {
            return;
        }

        var total = 0f;
        foreach (var column in columns)
        {
            total += Math.Max(1, column.Share);
        }

        var widths = new float[columns.Count];
        var wanted = 0f;
        for (var index = 0; index < columns.Count; index++)
        {
            widths[index] = Math.Max(MinimumChipWidth, available * Math.Max(1, columns[index].Share) / total);
            wanted += widths[index];
        }

        // Many narrow lanes cannot all keep their minimum width; squeezing keeps the bar inside the
        // card instead of letting the last chips run off it.
        var squeeze = wanted > available ? available / wanted : 1f;

        var x = area.X;
        for (var index = 0; index < columns.Count; index++)
        {
            var width = widths[index] * squeeze;
            DrawChip(canvas, new RectF(x, area.Y, width, area.Height), columns[index]);
            x += width + ChipGap;
        }
    }

    private void DrawChip(ICanvas canvas, RectF chip, WidgetColumn column)
    {
        canvas.FillColor = Paper(column.ColorIndex).WithAlpha((float)Math.Clamp(column.Opacity, 0.05, 1));
        canvas.FillRoundedRectangle(chip, ChipCornerRadius);

        DrawText(
            canvas,
            column.Count.ToString(CultureInfo.CurrentCulture),
            chip,
            ChipSize,
            semibold: true,
            palette.ChipInk,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
    }

    private void DrawText(
        ICanvas canvas,
        string text,
        RectF area,
        float size,
        bool semibold,
        Color color,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Top)
    {
        if (string.IsNullOrWhiteSpace(text) || area.Width <= 0f || area.Height <= 0f)
        {
            return;
        }

        canvas.Font = new GraphicsFont(semibold ? SemiboldFont : RegularFont, semibold ? SemiboldWeight : RegularWeight);
        canvas.FontSize = size;
        canvas.FontColor = color;
        canvas.DrawString(text, area.X, area.Y, area.Width, area.Height, horizontal, vertical, TextFlow.ClipBounds);
    }

    /// <summary>The paper palette, indexed exactly like <see cref="StickyNoteBorder.NoteColors"/>.</summary>
    private static Color Paper(int index)
    {
        var colors = StickyNoteBorder.NoteColors;
        var wrapped = index % colors.Count;
        return colors[wrapped < 0 ? wrapped + colors.Count : wrapped];
    }
}
