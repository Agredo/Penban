using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>Board overview page: lists existing boards, allows creating/deleting/opening them.</summary>
public partial class BoardsPage : ContentPage
{
    public BoardsPage(BoardsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is BoardsViewModel viewModel)
        {
            viewModel.LoadBoardsCommand.Execute(null);
        }
    }
}
