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
    /// Paper colours: the classic canary yellow, the other notes people actually buy, and a purple in
    /// place of the second orange. Bright in both themes on purpose - ink is stored dark, so the paper
    /// has to stay light.
    /// <para>
    /// This order is part of what is written down and must not be rearranged: a note keeps its colour
    /// as an index into this list, and a note that was never coloured has its index derived from its
    /// id, so moving a colour to another place would repaint every note that wears it. The picker lays
    /// the same colours out in hue order instead, see <see cref="PickerOrder"/>.
    /// </para>
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#FCF29B"),
        Color.FromArgb("#DCCBFF"),
        Color.FromArgb("#FFD6E8"),
        Color.FromArgb("#C8F2D4"),
        Color.FromArgb("#CBE2FF"),
        Color.FromArgb("#FFDCC0"),
    ];

    /// <summary>
    /// The paper colours in the order the picker puts them in: orange, yellow, green, blue, purple,
    /// pink. One walk through the hues, so no two neighbours are shades of the same colour, and the
    /// swatch a note wears is found by <see cref="NoteColorIndex"/> rather than by its place in the row.
    /// </summary>
    public static IReadOnlyList<int> PickerOrder { get; } = [5, 0, 3, 4, 1, 2];

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
