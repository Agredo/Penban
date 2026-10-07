namespace Penban.Models;

/// <summary>
/// A project collects boards under one heading and gets a page of its own. It carries the same
/// optional details a board can carry in its extended mode - tags and a time frame - and a note of
/// its own, so the dashboard can show a card for it that looks like a board card.
/// <para>
/// The boards of a project are not listed here: a board knows its <see cref="Board.ProjectId"/> and
/// nothing points the other way. That keeps one source of truth, leaves no orphan behind when a
/// board moves to another project, and lets a deleted project simply drop its boards.
/// </para>
/// </summary>
public class Project : SyncableEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Where the project sits in the list, ascending. Same meaning as <see cref="Board.SortOrder"/>:
    /// only the list writes it, and a project that was never moved carries the default <c>0</c> and is
    /// placed by the date it was last written to instead.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Free-text labels the user gave the project, trimmed and without duplicates (see
    /// <c>TagList.Normalize</c>). A label is what the projects list is filtered by, so the same label
    /// typed twice has to be one label - otherwise the filter would offer two chips that mean the
    /// same thing.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>First day the project runs, or <c>null</c> while the user has not said.</summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>Last day the project runs, or <c>null</c> while the user has not said.</summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// The project's own note, written from its page. Same as <see cref="Board.NoteStrokes"/>: empty
    /// until the user writes one, and a project without one shows a monogram on its card rather than
    /// an empty note.
    /// </summary>
    public List<InkStroke> NoteStrokes { get; set; } = new();

    /// <summary>
    /// Paper colour the user picked for <see cref="NoteStrokes"/>, or <c>null</c> while the colour is
    /// still derived from <see cref="SyncableEntity.Id"/>. A picked colour is kept when the note is
    /// emptied again; only dropping the note gives it up.
    /// </summary>
    public int? NoteColorIndex { get; set; }
}
