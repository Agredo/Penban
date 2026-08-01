using Penban.ViewModels;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Renders a single card. The card's ink drawing is hosted by an <see cref="Ink.InkCanvasHostView"/>
/// which is loaded/synced whenever the control's <see cref="BindableObject.BindingContext"/> changes.
/// Cross-column dragging is started through MAUI's native drag gesture on the handle.
/// </summary>
public partial class KanbanCardView : ContentView
{
    internal const string DraggedCardIdKey = "Penban.CardId";
    internal const string SourceColumnIdKey = "Penban.SourceColumnId";

    private CardViewModel? viewModel;

    public static readonly BindableProperty EnableNativeDragProperty =
        BindableProperty.Create(
            nameof(EnableNativeDrag),
            typeof(bool),
            typeof(KanbanCardView),
            true);

    public KanbanCardView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
        InkHost.StrokeCompleted += OnStrokeCompleted;
    }

    public bool EnableNativeDrag
    {
        get => (bool)GetValue(EnableNativeDragProperty);
        set => SetValue(EnableNativeDragProperty, value);
    }

    public Guid OwnerColumnId { get; set; }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        viewModel = BindingContext as CardViewModel;
        if (viewModel is not null)
        {
            InkHost.LoadStrokes(viewModel.InkCanvas.Strokes);
        }
    }

    private void OnStrokeCompleted(object? sender, EventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.InkCanvas.Clear();
        foreach (var stroke in InkHost.GetStrokes())
        {
            viewModel.InkCanvas.AddStroke(stroke);
        }

        _ = viewModel.SaveCommand.ExecuteAsync(null);
    }

    private void OnDragHandleDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (!EnableNativeDrag || viewModel is null || OwnerColumnId == Guid.Empty)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties[DraggedCardIdKey] = viewModel.Id;
        e.Data.Properties[SourceColumnIdKey] = OwnerColumnId;
    }
}
