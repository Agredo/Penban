// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Penban.Models;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One project the way the projects list and the dashboard show it: its heading, the labels on it,
/// its time frame and a single note that stands for the whole project.
/// <para>
/// The note is the project's own if it has one. A project without one gets a monogram instead of a
/// blank sheet - the letters of its title, on the same paper in the same tilt - so that a card of a
/// project nobody has written on still says which project it is.
/// </para>
/// </summary>
public partial class ProjectViewModel : ObservableObject
{
    private Project project;

    public ProjectViewModel(Project project, int boardCount, DateTimeOffset lastEditedUtc)
    {
        this.project = project;
        Id = project.Id;
        title = project.Title;
        SortOrder = project.SortOrder;
        boardCountValue = boardCount;
        LastEditedUtc = lastEditedUtc;

        BuildPreview();
    }

    public Guid Id { get; }

    /// <summary>
    /// Where this project sits in the list, ascending. Written only by
    /// <c>ProjectsViewModel.MoveProjectAsync</c> after a drag, same as on a board.
    /// </summary>
    public int SortOrder { get; set; }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private int boardCountValue;

    /// <summary>
    /// Most recent write to the project or to one of the boards under it. A board carries the work,
    /// so a project whose board was just written to is not a project that has stood still.
    /// </summary>
    public DateTimeOffset LastEditedUtc { get; private set; }

    /// <summary>The project's labels, in the order the user typed them.</summary>
    public IReadOnlyList<string> Tags => project.Tags;

    /// <summary>Whether there are labels to draw as chips.</summary>
    public bool HasTags => project.Tags.Count > 0;

    /// <summary>
    /// Whether the project has a time frame at all. A project without one holds no dates rather than
    /// a pair of default ones, so the card does not print a period nobody asked for.
    /// </summary>
    public bool HasPeriod => project.StartDate is not null || project.EndDate is not null;

    /// <summary>
    /// The project's time frame as one line, with the missing end of a half-open frame left out
    /// rather than filled in with a date the user never chose.
    /// </summary>
    public string PeriodText
    {
        get
        {
            var start = project.StartDate?.LocalDateTime.Date;
            var end = project.EndDate?.LocalDateTime.Date;

            return (start, end) switch
            {
                ({ } from, { } to) => $"{from:d} – {to:d}",
                ({ } from, null) => $"{from:d} –",
                (null, { } to) => $"– {to:d}",
                _ => string.Empty,
            };
        }
    }

    /// <summary>Whether the project carries a note of its own, written on its page.</summary>
    public bool HasNote => project.NoteStrokes.Count > 0;

    /// <summary>
    /// Paper colour of the project's note: the colour the user picked, or - as long as there is none
    /// - the one derived from the id, which stays the same for the lifetime of the project.
    /// </summary>
    public int NoteColorIndex => project.NoteColorIndex ?? NoteStyle.PaperIndexFor(Id);

    /// <summary>How many boards hang under the project, spelled out for the card's caption.</summary>
    public string BoardCountText => BoardCountValue switch
    {
        0 => Strings.NoBoardsInProject,
        1 => Strings.OneBoard,
        _ => string.Format(Strings.BoardsCountFormat, BoardCountValue),
    };

    /// <summary>
    /// Caption under the project title: when it was last touched and how much hangs under it. Same
    /// wording as on a board card, so the two lists read alike.
    /// </summary>
    public string SummaryText => BoardCountValue == 0
        ? Strings.NoBoardsInProject
        : string.Format(Strings.BoardSummaryFormat, RelativeTime.Describe(LastEditedUtc, DateTimeOffset.UtcNow), BoardCountText);

    /// <summary>The project's note, or the monogram standing in for it, as the card's one note.</summary>
    public ObservableCollection<BoardNotePreview> PreviewNotes { get; } = new();

    /// <summary>
    /// Whether the project carries every one of the given labels. The filter row narrows the list
    /// down rather than widening it, so a project has to carry all of them - picking a second label
    /// asks for the projects that have both.
    /// </summary>
    public bool HasAllTags(IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!project.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Brings this row up to date with the project as it was just read from the database, without
    /// giving up the view model itself - the list holds on to its rows so that it keeps the place
    /// the reader scrolled to (see <c>ProjectsViewModel.ReadProjectsAsync</c>).
    /// </summary>
    public void Refresh(Project project, int boardCount, DateTimeOffset lastEditedUtc)
    {
        this.project = project;
        Title = project.Title;
        SortOrder = project.SortOrder;
        BoardCountValue = boardCount;
        LastEditedUtc = lastEditedUtc;

        // Neither the labels nor the time frame have a field of their own to report from - they are
        // read off the project this row holds - so both are announced here.
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasPeriod));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(NoteColorIndex));
        OnPropertyChanged(nameof(LastEditedUtc));

        BuildPreview();
    }

    /// <summary>
    /// Builds the one note of the card: the project's own note where there is one, its monogram
    /// where there is not. A project with neither - a title of nothing but spaces - keeps the blank
    /// sheet, which is what a board without a note shows as well.
    /// </summary>
    private void BuildPreview()
    {
        var colorIndex = project.NoteColorIndex;

        PreviewNotes.Clear();

        if (HasNote)
        {
            PreviewNotes.Add(new BoardNotePreview(Id, project.NoteStrokes, colorIndex, 0, 1));
        }
        else if (Monogram.From(project.Title) is { Length: > 0 } monogram)
        {
            PreviewNotes.Add(new BoardNotePreview(Id, monogram, colorIndex, 0, 1));
        }
        else
        {
            PreviewNotes.Add(new BoardNotePreview(new Card { Id = Id, NoteColorIndex = colorIndex }, 0, 1));
        }
    }

    partial void OnBoardCountValueChanged(int value)
    {
        OnPropertyChanged(nameof(BoardCountText));
        OnPropertyChanged(nameof(SummaryText));
    }
}
