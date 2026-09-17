using Microsoft.Maui.Controls.Shapes;
using Penban.Util;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Draws its content as a physical sticky note: square paper, square-ish corners, a soft drop
/// shadow and a slight tilt. Everything else about the note (size, padding, content) is left to
/// the page.
/// </summary>
public class StickyNoteBorder : Border
{
    /// <summary>
    /// Paper colours: the classic canary yellow twice, plus the other notes people actually buy.
    /// Bright in both themes on purpose - ink is stored dark, so the paper has to stay light.
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#FCF29B"),
        Color.FromArgb("#FFE9A8"),
        Color.FromArgb("#FFD6E8"),
        Color.FromArgb("#C8F2D4"),
        Color.FromArgb("#CBE2FF"),
        Color.FromArgb("#FFDCC0"),
    ];

    /// <summary>All paper colours, indexed exactly like <see cref="NoteStyle.PaperIndexFor"/>.</summary>
    public static IReadOnlyList<Color> NoteColors => Palette;

    public static readonly BindableProperty NoteColorIndexProperty = BindableProperty.Create(
        nameof(NoteColorIndex),
        typeof(int),
        typeof(StickyNoteBorder),
        0,
        propertyChanged: (bindable, _, _) => ((StickyNoteBorder)bindable).ApplyPaper());

    public static readonly BindableProperty NoteTiltProperty = BindableProperty.Create(
        nameof(NoteTilt),
        typeof(double),
        typeof(StickyNoteBorder),
        0d,
        propertyChanged: (bindable, _, value) => ((StickyNoteBorder)bindable).Rotation = (double)value);

    public StickyNoteBorder()
    {
        // A note has no outline; the shadow alone lifts it off the board.
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) };
        Shadow = new Shadow
        {
            Brush = Brush.Black,
            Opacity = 0.22f,
            Radius = 6,
            Offset = new Point(0, 3),
        };
        ApplyPaper();
    }

    /// <summary>Index into the paper palette, see <see cref="NoteStyle.PaperIndexFor"/>.</summary>
    public int NoteColorIndex
    {
        get => (int)GetValue(NoteColorIndexProperty);
        set => SetValue(NoteColorIndexProperty, value);
    }

    /// <summary>Tilt of the note in degrees, see <see cref="NoteStyle.TiltFor"/>.</summary>
    public double NoteTilt
    {
        get => (double)GetValue(NoteTiltProperty);
        set => SetValue(NoteTiltProperty, value);
    }

    private void ApplyPaper()
    {
        var index = NoteColorIndex % Palette.Length;
        Background = Palette[index < 0 ? index + Palette.Length : index];
    }
}
