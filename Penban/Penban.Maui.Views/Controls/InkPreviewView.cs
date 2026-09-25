using System.Collections.Specialized;
using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Lightweight, read-only ink preview for board cards. The drawing itself lives in
/// <see cref="InkRenderer"/>, which the widget snapshots use as well, so a note looks the same
/// wherever it is rendered.
/// </summary>
public sealed class InkPreviewView : GraphicsView, IDrawable
{
    public static readonly BindableProperty StrokesSourceProperty = BindableProperty.Create(
        nameof(StrokesSource),
        typeof(IEnumerable<InkStroke>),
        typeof(InkPreviewView),
        default(IEnumerable<InkStroke>),
        propertyChanged: OnStrokesSourceChanged);

    private INotifyCollectionChanged? observedCollection;

    public InkPreviewView()
    {
        Drawable = this;
    }

    public IEnumerable<InkStroke>? StrokesSource
    {
        get => (IEnumerable<InkStroke>?)GetValue(StrokesSourceProperty);
        set => SetValue(StrokesSourceProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect) => InkRenderer.Draw(canvas, StrokesSource, dirtyRect);

    private static void OnStrokesSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (InkPreviewView)bindable;
        view.UnsubscribeFromObservedCollection();
        view.SubscribeToObservedCollection(newValue as INotifyCollectionChanged);
        view.Invalidate();
    }

    private void SubscribeToObservedCollection(INotifyCollectionChanged? collection)
    {
        observedCollection = collection;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += OnObservedCollectionChanged;
        }
    }

    private void UnsubscribeFromObservedCollection()
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= OnObservedCollectionChanged;
            observedCollection = null;
        }
    }

    private void OnObservedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Invalidate();
}
