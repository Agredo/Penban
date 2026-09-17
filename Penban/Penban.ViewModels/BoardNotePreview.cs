// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One note of the small stack that stands in for a board on the overview. Each note is a real card
/// of the board, so it keeps its own paper colour and its own hand-placed tilt - the fan only adds
/// the sideways push that turns a pile into a hand of notes.
/// </summary>
public sealed class BoardNotePreview
{
    /// <summary>
    /// Places of the fan per note count, in the order the notes are stacked: everything before the
    /// last entry ends up behind it. The widest fan therefore has its two outer notes in the back
    /// and the middle one on top, which is how a hand of notes actually looks.
    /// </summary>
    private static readonly double[][] Slots =
    {
        new[] { 0d },
        new[] { -0.5, 0.5 },
        new[] { -1d, 1d, 0d },
    };

    /// <summary>Sideways distance between two neighbouring places of the fan.</summary>
    private const double FanSpacing = 30;

    /// <summary>Degrees each place of the fan is turned further than its neighbour.</summary>
    private const double FanAngle = 8;

    /// <summary>How far the outer notes sit above the front one.</summary>
    private const double FanLift = 3;

    public BoardNotePreview(Card card, int index, int count)
    {
        Strokes = card.Strokes;
        NoteColorIndex = card.NoteColorIndex ?? NoteStyle.PaperIndexFor(card.Id);

        var slots = Slots[Math.Clamp(count, 1, Slots.Length) - 1];
        var slot = slots[Math.Clamp(index, 0, slots.Length - 1)];

        Tilt = NoteStyle.TiltFor(card.Id) + (slot * FanAngle);
        OffsetX = slot * FanSpacing;
        OffsetY = slot == 0 ? 0 : -FanLift;
    }

    /// <summary>Ink of this note.</summary>
    public IReadOnlyList<InkStroke> Strokes { get; }

    /// <summary>Paper colour of this note, see <see cref="NoteStyle.PaperIndexFor"/>.</summary>
    public int NoteColorIndex { get; }

    /// <summary>Rotation of this note in degrees: tilt of the card plus its place in the fan.</summary>
    public double Tilt { get; }

    /// <summary>Push away from the middle of the fan, horizontally.</summary>
    public double OffsetX { get; }

    /// <summary>Push away from the middle of the fan, vertically.</summary>
    public double OffsetY { get; }
}
