// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// The project's own note, written from its page. Same kind of ink as the note of a board and stored
/// the same way, only on the project: a project that has never been written on shows a monogram on
/// its card, and writing here is what puts the user's own hand in its place.
/// </summary>
public partial class ProjectNoteViewModel : ObservableObject, INoteEditorTarget
{
    private readonly Project project;
    private readonly IProjectService projectService;
    private readonly IDialogService dialogService;

    public ProjectNoteViewModel(Project project, IProjectService projectService, IDialogService dialogService)
    {
        this.project = project;
        this.projectService = projectService;
        this.dialogService = dialogService;
        InkCanvas = new InkCanvasViewModel();
        InkCanvas.LoadStrokes(project.NoteStrokes);
    }

    public Guid Id => project.Id;

    /// <summary>
    /// Paper colour of the project's note: the colour the user picked, or - as long as there is none -
    /// the one derived from the id, which stays the same for the lifetime of the project.
    /// </summary>
    public int NoteColorIndex
    {
        get => project.NoteColorIndex ?? NoteStyle.PaperIndexFor(project.Id);
        set
        {
            if (project.NoteColorIndex == value)
            {
                return;
            }

            project.NoteColorIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The picked colour, see <see cref="INoteEditorTarget.StoredNoteColorIndex"/>.</summary>
    public int? StoredNoteColorIndex => project.NoteColorIndex;

    public bool SupportsTags => false;

    public IReadOnlyList<string> Tags => [];

    public bool ToggleTag(string tag) => false;

    /// <summary>Set once the note has been dropped, so the editor stops writing.</summary>
    [ObservableProperty]
    private bool isDeleted;

    public InkCanvasViewModel InkCanvas { get; }

    /// <summary>
    /// Writes the ink to the project. Only the ink is written: the paper colour is the user's choice
    /// and stays with the project even while the note is empty, so clearing it here would repaint the
    /// project's card behind the user's back.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var strokes = InkCanvas.Strokes.ToList();

        project.NoteStrokes = strokes;

        await projectService.SaveProjectNoteAsync(project.Id, strokes, project.NoteColorIndex);
    }

    /// <summary>
    /// Drops the note from the project. Unlike erasing the last stroke, which only takes the ink away,
    /// this takes the note together with the paper colour the user gave it, so the project's card goes
    /// back to the monogram it showed before.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteProjectNoteConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        project.NoteStrokes = [];
        project.NoteColorIndex = null;
        InkCanvas.Clear();
        await projectService.SaveProjectNoteAsync(project.Id, [], null);

        IsDeleted = true;
    }
}
