using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using CommunityToolkit.WinUI.Collections;
using Windows.Foundation.Collections;

namespace Kronos.Collections;

/// <summary>
/// An <see cref="AdvancedCollectionView"/> re-announced through <see cref="INotifyCollectionChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// The games grid is a grouped CollectionViewSource whose groups are AdvancedCollectionViews, filled after
/// the grid is bound, as the libraries load. AdvancedCollectionView reports changes only through
/// <c>IObservableVector.VectorChanged</c>. WinUI's grouped view listens for that; Uno's listens only for
/// <c>INotifyCollectionChanged</c> on each group. So on Uno the grid's item count went up while no item
/// was ever realised, and the Games page was simply empty - measured in spike/Kronos.UnoSpike
/// (GroupedGridProbe): every other combination works, and only this one fails.
/// </para>
/// <para>
/// Each change is translated rather than collapsed into a Reset, so adding one game does not rebuild every
/// container in its group. A snapshot of the view is kept because a Remove has to carry the removed item,
/// and by the time VectorChanged fires it is already gone from the view.
/// </para>
/// <para>
/// The view's filtering and sorting are untouched: this only reads it. Changes are raised on whichever
/// thread changed the view, which is the UI thread wherever Kronos changes the game list.
/// </para>
/// </remarks>
public sealed class NotifyingCollectionView : IList, INotifyCollectionChanged
{
    readonly AdvancedCollectionView _view;
    readonly List<object> _snapshot;

    public NotifyingCollectionView(AdvancedCollectionView view)
    {
        _view = view;
        _snapshot = new List<object>(view);
        _view.VectorChanged += View_VectorChanged;
    }

    /// <summary>The view this re-announces.</summary>
    public AdvancedCollectionView View => _view;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    void View_VectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        var index = (int)args.Index;
        NotifyCollectionChangedEventArgs change;

        switch (args.CollectionChange)
        {
            case CollectionChange.ItemInserted when index >= 0 && index <= _snapshot.Count && index < _view.Count:
            {
                var item = _view[index];
                _snapshot.Insert(index, item);
                change = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index);
                break;
            }

            case CollectionChange.ItemRemoved when index >= 0 && index < _snapshot.Count:
            {
                var item = _snapshot[index];
                _snapshot.RemoveAt(index);
                change = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index);
                break;
            }

            case CollectionChange.ItemChanged when index >= 0 && index < _snapshot.Count && index < _view.Count:
            {
                var oldItem = _snapshot[index];
                var newItem = _view[index];
                _snapshot[index] = newItem;
                change = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItem, oldItem, index);
                break;
            }

            default:
                // A Reset, or an index that no longer lines up with the snapshot. Either way the honest
                // answer is to start again from the view as it is now.
                _snapshot.Clear();
                _snapshot.AddRange(_view);
                change = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                break;
        }

        CollectionChanged?.Invoke(this, change);
    }

    // Read from the snapshot, not the view, so what a listener reads always agrees with the last event it
    // was given.
    public object? this[int index]
    {
        get => _snapshot[index];
        set => throw new NotSupportedException("The games view is read-only.");
    }

    public int Count => _snapshot.Count;

    public bool IsFixedSize => false;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public bool Contains(object? value) => value is not null && _snapshot.Contains(value);

    public int IndexOf(object? value) => value is null ? -1 : _snapshot.IndexOf(value);

    public void CopyTo(Array array, int index) => ((ICollection)_snapshot).CopyTo(array, index);

    public IEnumerator GetEnumerator() => _snapshot.GetEnumerator();

    public int Add(object? value) => throw new NotSupportedException("The games view is read-only.");

    public void Clear() => throw new NotSupportedException("The games view is read-only.");

    public void Insert(int index, object? value) => throw new NotSupportedException("The games view is read-only.");

    public void Remove(object? value) => throw new NotSupportedException("The games view is read-only.");

    public void RemoveAt(int index) => throw new NotSupportedException("The games view is read-only.");
}
