using System.Windows.Input;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.ViewModels;

namespace Penban.Maui.Views.Services;

/// <summary>
/// What can be done with a board from the card that stands for it: send it away as a file, write on it,
/// throw it away. Every page that lists boards hands its cards the same three commands, so a button
/// means the same thing wherever a card turns up.
/// </summary>
/// <remarks>
/// The three live here rather than on each view model because they are not about a list: sending away
/// and opening the details page both leave the page that shows the list, and throwing a board away is
/// the one thing a page does differently. That last one is therefore passed in, so a page keeps the
/// delete it already has.
/// </remarks>
public sealed class BoardCardActions
{
    public BoardCardActions(TransferCoordinator transferCoordinator, INavigationService navigation, ICommand deleteBoard)
    {
        ShareCommand = new AsyncRelayCommand<BoardViewModel>(board => board is null
            ? Task.CompletedTask
            : transferCoordinator.ShowBoardExportMenuAsync(board.Id));

        EditCommand = new AsyncRelayCommand<BoardViewModel>(board => board is null
            ? Task.CompletedTask
            : navigation.ShellNavigationTo(
                AppRoutes.BoardDetails,
                new Dictionary<string, object> { [QueryParameters.BoardId] = board.Id }));

        DeleteCommand = new RelayCommand<BoardViewModel>(board =>
        {
            if (board is not null)
            {
                deleteBoard.Execute(board.Id);
            }
        });
    }

    /// <summary>Sends the board away as a file the user picks the target of.</summary>
    public ICommand ShareCommand { get; }

    /// <summary>Opens the details page on the board, where everything written on it can be changed.</summary>
    public ICommand EditCommand { get; }

    /// <summary>Throws the board away, after asking. Runs the page's own delete command.</summary>
    public ICommand DeleteCommand { get; }
}
