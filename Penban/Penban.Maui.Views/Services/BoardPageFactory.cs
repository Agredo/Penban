using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Penban.Maui.Views.Pages;
using Penban.Services.Abstractions;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Services;

/// <summary>
/// Builds the page of a single board. A board is opened from more than one place - the board
/// overview, a project's own page and the search - and its page wants a stack of services besides the
/// board it shows. Putting it together is kept in one place, so that a dependency added to
/// <see cref="BoardPage"/> is added here once instead of being missed at one of the call sites.
/// </summary>
public class BoardPageFactory(
    SettingsViewModel settingsViewModel,
    FeedbackViewModel feedbackViewModel,
    IPreferences preferences,
    TransferCoordinator transferCoordinator,
    IDialogService dialogService,
    INavigationService navigation,
    SearchViewModel searchViewModel)
{
    /// <summary>
    /// The page showing one board.
    /// </summary>
    /// <param name="board">The board to show.</param>
    /// <param name="openCardId">
    /// A note on that board to open on top of it, or <c>null</c> to show the board itself.
    /// </param>
    public BoardPage Create(BoardViewModel board, Guid? openCardId = null) =>
        new(board, settingsViewModel, feedbackViewModel, preferences, transferCoordinator, dialogService, navigation, searchViewModel, openCardId);
}
