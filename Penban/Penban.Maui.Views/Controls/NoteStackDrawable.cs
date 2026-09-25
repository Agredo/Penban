namespace Penban.Maui.Views.Controls;

/// <summary>
/// Draws the stack of notes that stands in for a board, the same way the board card on the overview
/// fans them out: each <see cref="NoteStackItem"/> carries its own place, tilt and paper colour, this
/// only puts paper, shadow and ink on the canvas.
/// <para>
/// It exists so the home screen widget can show the very same thumbnail as the app. The widget gets
/// an image instead of a view tree, which keeps a second renderer of the ink out of the extension.
/// </para>
/// </summary>
public sealed class NoteStackDrawable : IDrawable
{
    /// <summary>Side of one note; the widget sizes its frame in these units.</summary>
    public const float NoteSize = 110f;

    /// <summary>Space between the paper edge and the ink, so a note never looks written to the rim.</summary>
    public const float NotePadding = 10f;

    /// <summary>
    /// Size of the frame the notes are fanned out in. Deliberately bigger than one note, so the outer
    /// notes of the fan and their shadows have room. Matches the thumbnail on the board card.
    /// </summary>
    public const float FrameWidth = 196f;

    /// <summary>See <see cref="FrameWidth"/>.</summary>
    public const float FrameHeight = 146f;

    private const float CornerRadius = 3f;

    private static readonly Color ShadowColor = Color.FromRgba(0f, 0f, 0f, 0.22f);

    private readonly IReadOnlyList<NoteStackItem> notes;

    public NoteStackDrawable(IReadOnlyList<NoteStackItem> notes)
    {
        this.notes = notes;
    }

    /// <summary>The paper palette, indexed exactly like <see cref="StickyNoteBorder.NoteColors"/>.</summary>
    private static IReadOnlyList<Color> Paper => StickyNoteBorder.NoteColors;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (notes.Count == 0 || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(dirtyRect.Width / FrameWidth, dirtyRect.Height / FrameHeight);

        canvas.SaveState();
        canvas.Translate(dirtyRect.Center.X, dirtyRect.Center.Y);
        canvas.Scale(scale, scale);

        // In order, so the last note - the one the board's own note takes - ends up on top.
        foreach (var note in notes)
        {
            canvas.SaveState();
            canvas.Translate(note.OffsetX, note.OffsetY);
            canvas.Rotate(note.Tilt);
            DrawNote(canvas, note);
            canvas.RestoreState();
        }

        canvas.RestoreState();
    }

    private static void DrawNote(ICanvas canvas, NoteStackItem note)
    {
        var half = NoteSize / 2f;
        var paper = new RectF(-half, -half, NoteSize, NoteSize);

        var index = note.ColorIndex % Paper.Count;
        var color = Paper[index < 0 ? index + Paper.Count : index];

        // Shadow first, and in its own state: the shadow would otherwise sit under the ink as well and
        // darken the drawing.
        canvas.SaveState();
        canvas.SetShadow(new SizeF(0f, 3f), 6f, ShadowColor);
        canvas.FillColor = color;
        canvas.FillRoundedRectangle(paper, CornerRadius);
        canvas.RestoreState();

        var ink = new RectF(
            paper.X + NotePadding,
            paper.Y + NotePadding,
            paper.Width - (NotePadding * 2f),
            paper.Height - (NotePadding * 2f));
        if (ink.Width <= 0 || ink.Height <= 0)
        {
            return;
        }

        var clip = new PathF();
        clip.AppendRoundedRectangle(paper, CornerRadius);

        canvas.SaveState();
        canvas.ClipPath(clip);
        InkRenderer.Draw(canvas, note.Strokes, ink);
        canvas.RestoreState();
    }
}
