using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ReadestStats.ViewModels;

/// <summary>
/// An ObservableCollection that supports high-performance batch updates
/// (ReplaceRange, AddRange) with suppressed intermediate notifications,
/// raising a single Reset notification at the end.
/// Eliminates UI thread event storms when updating large collections in WPF.
/// Automatically dispatches to the WPF UI thread if called from a background thread.
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public RangeObservableCollection() : base() { }
    public RangeObservableCollection(IEnumerable<T> collection) : base(collection) { }
    public RangeObservableCollection(List<T> list) : base(list) { }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnPropertyChanged(e);
    }

    protected override void ClearItems()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ClearItems);
            return;
        }
        base.ClearItems();
    }

    protected override void InsertItem(int index, T item)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => InsertItem(index, item));
            return;
        }
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => RemoveItem(index));
            return;
        }
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, T item)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SetItem(index, item));
            return;
        }
        base.SetItem(index, item);
    }

    /// <summary>
    /// Replaces all items in the collection with the specified collection in a single batch,
    /// firing only one Reset notification to avoid UI thread event storms.
    /// </summary>
    public void ReplaceRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ReplaceRange(collection));
            return;
        }

        var itemsList = collection as IList<T> ?? collection.ToList();

        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in itemsList)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds a range of items to the collection in a single batch,
    /// firing only one Reset notification.
    /// </summary>
    public void AddRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => AddRange(collection));
            return;
        }

        var itemsList = collection as IList<T> ?? collection.ToList();
        if (itemsList.Count == 0) return;

        _suppressNotification = true;
        try
        {
            foreach (var item in itemsList)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
