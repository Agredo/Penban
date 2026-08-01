using System.Collections.Specialized;
using Penban.ViewModels;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Renders a column header, its ordered cards, and an add-card affordance. Cards are added
/// manually, while cross-column card moves are handled through MAUI's native drop gesture on the card host.
/// </summary>
public partial class KanbanColumnView : ContentView
{
    private ColumnViewModel? viewModel;

    public KanbanColumnView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    /// <summary>Raised when the user taps the column's delete (✕) button.</summary>
    public event EventHandler<Guid>? DeleteRequested;

    /// <summary>Raised when a card is dropped onto this column.</summary>
    public event EventHandler<CardDropRequest>? CardDropRequested;

    public View ColumnReorderHandle => HeaderHost;

    public Guid ColumnId => viewModel?.Id ?? Guid.Empty;

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.Cards.CollectionChanged -= OnCardsChanged;
        }

        viewModel = BindingContext as ColumnViewModel;
        CardsHost.Children.Clear();

        if (viewModel is null)
        {
            return;
        }

        foreach (var card in viewModel.Cards)
        {
            CardsHost.Children.Add(CreateCardView(card));
        }

        viewModel.Cards.CollectionChanged += OnCardsChanged;
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CardsHost.Children.Clear();
        if (viewModel is null)
        {
            return;
        }

        foreach (var card in viewModel.Cards)
        {
            CardsHost.Children.Add(CreateCardView(card));
        }
    }

    private View CreateCardView(CardViewModel card)
    {
        var cardView = new KanbanCardView
        {
            BindingContext = card,
            OwnerColumnId = viewModel?.Id ?? Guid.Empty,
        };
        var drop = new DropGestureRecognizer
        {
            AllowDrop = true,
        };
        drop.DragOver += OnCardsHostDragOver;
        drop.Drop += OnCardDrop;
        cardView.GestureRecognizers.Add(drop);
        return cardView;
    }

    private void OnCardsHostDragOver(object? sender, DragEventArgs e)
        => e.AcceptedOperation = DataPackageOperation.Copy;

    private void OnCardsHostDrop(object? sender, DropEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        if (!TryGetGuid(e.Data.Properties, KanbanCardView.DraggedCardIdKey, out var cardId) ||
            !TryGetGuid(e.Data.Properties, KanbanCardView.SourceColumnIdKey, out var sourceColumnId))
        {
            return;
        }

        e.Handled = true;
        CardDropRequested?.Invoke(this, new CardDropRequest(cardId, sourceColumnId, viewModel.Id, viewModel.Cards.Count));
    }

    private void OnCardDrop(object? sender, DropEventArgs e)
    {
        if (viewModel is null || sender is not KanbanCardView targetCardView || targetCardView.BindingContext is not CardViewModel targetCard)
        {
            return;
        }

        if (!TryGetGuid(e.Data.Properties, KanbanCardView.DraggedCardIdKey, out var cardId) ||
            !TryGetGuid(e.Data.Properties, KanbanCardView.SourceColumnIdKey, out var sourceColumnId))
        {
            return;
        }

        var targetIndex = viewModel.Cards.IndexOf(targetCard);
        if (targetIndex < 0)
        {
            targetIndex = viewModel.Cards.Count;
        }

        e.Handled = true;
        CardDropRequested?.Invoke(this, new CardDropRequest(cardId, sourceColumnId, viewModel.Id, targetIndex));
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, object> properties, string key, out Guid value)
    {
        if (!properties.TryGetValue(key, out var rawValue))
        {
            value = Guid.Empty;
            return false;
        }

        switch (rawValue)
        {
            case Guid guid:
                value = guid;
                return true;
            case string text when Guid.TryParse(text, out var parsed):
                value = parsed;
                return true;
            default:
                return Guid.TryParse(rawValue.ToString(), out value);
        }
    }

    private void OnDeleteColumnClicked(object? sender, EventArgs e)
    {
        if (viewModel is not null)
        {
            DeleteRequested?.Invoke(this, viewModel.Id);
        }
    }
}
