using Penban.Models;

namespace Penban.Maui.Views.Ink;

/// <summary>What a recorded edit did to a stroke set.</summary>
public enum InkEditKind
{
    /// <summary>The stroke was put on top of the set.</summary>
    Added,

    /// <summary>The strokes were taken out of the set.</summary>
    Erased,
}

/// <summary>A stroke together with the position it had in the stroke set.</summary>
/// <param name="Index">Position in the stroke set at the time of the edit.</param>
/// <param name="Stroke">The stroke itself.</param>
public readonly record struct InkStrokePlacement(int Index, InkStroke Stroke);

/// <summary>One reversible change to a stroke set.</summary>
public sealed class InkEdit
{
    public InkEdit(InkEditKind kind, IReadOnlyList<InkStrokePlacement> placements)
    {
        Kind = kind;
        Placements = placements;
    }

    public InkEditKind Kind { get; }

    public IReadOnlyList<InkStrokePlacement> Placements { get; }
}

/// <summary>
/// Undo/redo history for a single stroke set.
/// <para>
/// The renderers used to undo by removing the last stroke of the set, which deleted the wrong stroke
/// as soon as erasing was involved: an erase never entered the history, so the undo after an erase
/// removed the stroke that had been drawn *after* the erased one and left the erased stroke gone for
/// good. Every change now goes through this stack, and an erase records the removed strokes together
/// with the index they had, so redo puts them back where they were instead of appending them.
/// </para>
/// </summary>
public sealed class InkCommandStack
{
    /// <summary>How many edits are remembered; older ones fall off the bottom of the history.</summary>
    private const int MaxDepth = 100;

    private readonly ObservableStrokeCollection strokes;
    private readonly List<InkEdit> done = new();
    private readonly List<InkEdit> undone = new();

    public InkCommandStack(ObservableStrokeCollection strokes) => this.strokes = strokes;

    public bool CanUndo => done.Count > 0;

    public bool CanRedo => undone.Count > 0;

    /// <summary>Records a stroke that was just added to the set.</summary>
    public void RecordAdded(InkStroke stroke) =>
        Push(new InkEdit(InkEditKind.Added, new[] { new InkStrokePlacement(strokes.IndexOf(stroke), stroke) }));

    /// <summary>
    /// Records strokes that were just removed from the set. The placements have to carry the index
    /// the stroke had before it was removed, which the eraser knows because it walks the set backwards.
    /// </summary>
    public void RecordErased(IReadOnlyList<InkStrokePlacement> placements)
    {
        if (placements.Count == 0)
        {
            return;
        }

        Push(new InkEdit(InkEditKind.Erased, placements.ToList()));
    }

    /// <summary>
    /// Forgets the most recent edit that added <paramref name="stroke"/>, without touching the set.
    /// Used when a stroke is withdrawn again before it is ever finished - the pen tail eraser starts a
    /// stroke and drops it in the same breath - so the history does not keep an edit for a stroke that
    /// is not there.
    /// </summary>
    public void Discard(InkStroke stroke)
    {
        for (var i = done.Count - 1; i >= 0; i--)
        {
            var edit = done[i];
            if (edit.Kind != InkEditKind.Added)
            {
                continue;
            }

            if (edit.Placements.Any(p => ReferenceEquals(p.Stroke, stroke)))
            {
                done.RemoveAt(i);
                return;
            }
        }
    }

    public bool Undo()
    {
        if (done.Count == 0)
        {
            return false;
        }

        var edit = done[^1];
        done.RemoveAt(done.Count - 1);
        Apply(edit, forward: false);
        undone.Add(edit);
        return true;
    }

    public bool Redo()
    {
        if (undone.Count == 0)
        {
            return false;
        }

        var edit = undone[^1];
        undone.RemoveAt(undone.Count - 1);
        Apply(edit, forward: true);
        done.Add(edit);
        return true;
    }

    /// <summary>Throws the whole history away, e.g. after the stroke set was replaced wholesale.</summary>
    public void Reset()
    {
        done.Clear();
        undone.Clear();
    }

    private void Push(InkEdit edit)
    {
        done.Add(edit);
        if (done.Count > MaxDepth)
        {
            done.RemoveAt(0);
        }

        // A new edit makes the redo branch unreachable - that is the usual linear history.
        undone.Clear();
    }

    private void Apply(InkEdit edit, bool forward)
    {
        // "Added" inserts when it is replayed forwards and removes when it is undone; "Erased" is
        // the other way round. Both directions therefore share the same two operations.
        var insert = (edit.Kind == InkEditKind.Added) == forward;

        if (insert)
        {
            // Ascending, so the strokes end up in the order they were in before.
            foreach (var placement in edit.Placements.OrderBy(p => p.Index))
            {
                strokes.Insert(Math.Clamp(placement.Index, 0, strokes.Count), placement.Stroke);
            }

            return;
        }

        foreach (var placement in edit.Placements)
        {
            strokes.Remove(placement.Stroke);
        }
    }
}
