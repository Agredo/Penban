using System.Globalization;

namespace Penban.Models;

/// <summary>
/// One of the pens on the editor's toolbar: the colour it writes in and how wide it writes. Both live
/// in the slot rather than apart, so picking a pen picks the colour and the width together - what is
/// chosen is a pen, not a colour and a width that happen to be set at the same time.
/// </summary>
public readonly record struct PenSlot(string ColorHex, float Thickness)
{
    /// <summary>Colour the first pen starts with: the ink a note is written in by default.</summary>
    public const string FirstColorHex = "#000000";

    /// <summary>Colour the second pen starts with, so the two are told apart at a glance.</summary>
    public const string SecondColorHex = "#C62828";

    /// <summary>Width the second pen starts with: wider than the first, so the pair reads as two pens.</summary>
    public const float SecondThickness = 7f;
}

/// <summary>
/// The pens of the toolbar as one stored value: reading, writing, and the pair a store that has none
/// yet starts with.
/// </summary>
public static class PenSlots
{
    /// <summary>Number of pens the toolbar shows.</summary>
    public const int Count = 2;

    private const char SlotSeparator = ';';
    private const char FieldSeparator = '|';

    /// <summary>The pens a store that has none yet starts with.</summary>
    public static IReadOnlyList<PenSlot> Defaults { get; } =
    [
        new(PenSlot.FirstColorHex, PenThickness.Default),
        new(PenSlot.SecondColorHex, PenSlot.SecondThickness),
    ];

    /// <summary>Writes the pens as one value: <c>#RRGGBB|width;#RRGGBB|width</c>.</summary>
    public static string Format(IReadOnlyList<PenSlot> slots) =>
        string.Join(
            SlotSeparator,
            slots.Select(slot => string.Join(FieldSeparator, slot.ColorHex, FormatThickness(slot.Thickness))));

    /// <summary>
    /// Reads the stored value back. Anything unreadable falls back to the default of that slot, so a
    /// store that was written by an older version still leaves the whole toolbar standing.
    /// </summary>
    public static IReadOnlyList<PenSlot> Parse(string? stored)
    {
        var parts = (stored ?? string.Empty).Split(SlotSeparator, StringSplitOptions.RemoveEmptyEntries);
        var slots = new PenSlot[Count];

        for (var index = 0; index < Count; index++)
        {
            slots[index] = index < parts.Length && TryParseSlot(parts[index], out var slot)
                ? slot
                : Defaults[index];
        }

        return slots;
    }

    private static bool TryParseSlot(string text, out PenSlot slot)
    {
        slot = default;

        var fields = text.Split(FieldSeparator);
        if (fields.Length != 2 || !PenColor.TryNormalize(fields[0], out var hex))
        {
            return false;
        }

        if (!float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness))
        {
            return false;
        }

        slot = new PenSlot(hex, PenThickness.Clamp(thickness));
        return true;
    }

    private static string FormatThickness(float thickness) =>
        thickness.ToString("0.##", CultureInfo.InvariantCulture);
}
