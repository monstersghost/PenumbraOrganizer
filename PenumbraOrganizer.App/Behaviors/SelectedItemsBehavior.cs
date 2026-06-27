namespace PenumbraOrganizer.App.Behaviors;

using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

public static class SelectedItemsBehavior
{
    public static readonly DependencyProperty BoundSelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "BoundSelectedItems",
            typeof(IList),
            typeof(SelectedItemsBehavior),
            new PropertyMetadata(null, OnBoundSelectedItemsChanged));

    public static IList? GetBoundSelectedItems(DependencyObject obj)
        => (IList?)obj.GetValue(BoundSelectedItemsProperty);

    public static void SetBoundSelectedItems(DependencyObject obj, IList? value)
        => obj.SetValue(BoundSelectedItemsProperty, value);

    private static void OnBoundSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
            return;

        dataGrid.SelectionChanged -= DataGridSelectionChanged;
        dataGrid.SelectionChanged += DataGridSelectionChanged;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= BoundCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += BoundCollectionChanged;
    }

    private static void DataGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
            return;

        var bound = GetBoundSelectedItems(dataGrid);
        if (bound is null)
            return;

        foreach (var item in e.RemovedItems)
            bound.Remove(item);
        foreach (var item in e.AddedItems)
        {
            if (!bound.Contains(item))
                bound.Add(item);
        }
    }

    private static void BoundCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The DataGrid remains the source of truth for selection. This hook keeps
        // the binding alive when the view-model collection is cleared directly.
    }
}
