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
/// Every change is announced as a Reset. This used to translate each change into an Add, Remove or Replace
/// at the right index, and the translation was correct - replaying the events gives exactly the view's
/// contents (NotifyingCollectionViewTests) - but Uno's grouped grid applies an incremental change in any
/// group after the first at the wrong place. On a library with Steam, Xbox and manually added games, the
/// Xbox and manual tiles were realised among the Steam ones and bound to the first Steam game, so the
/// Games page showed one cover several times over and lost the games it should have shown. A Reset is
/// the one notification Uno's grouped grid handles correctly, which is why searching made the grid right.
/// </para>
/// <para>
/// A Reset rebuilds the group's containers, and loading a library changes every game several times, so
/// when a scheduler is supplied a burst of changes is coalesced into one Reset that runs after it. Without
/// one (tests) each change resets immediately.
/// </para>
/// <para>
/// The view's filtering and sorting are untouched: this only reads it.
/// </para>
/// </remarks>
public sealed class NotifyingCollectionView : IList, INotifyCollectionChanged
{
    readonly AdvancedCollectionView _view;
    readonly List<object> _snapshot;
    readonly Func<Action, bool>? _schedule;
    bool _resetPending;

    /// <param name="view">The view to re-announce.</param>
    /// <param name="schedule">
    /// Queues an action to run later on the thread that owns the view, returning false if it could not be
    /// queued. Used to coalesce bursts of changes into a single Reset.
    /// </param>
    public NotifyingCollectionView(AdvancedCollectionView view, Func<Action, bool>? schedule = null)
    {
        _view = view;
        _schedule = schedule;
        _snapshot = new List<object>(view);
        _view.VectorChanged += View_VectorChanged;
    }

    /// <summary>The view this re-announces.</summary>
    public AdvancedCollectionView View => _view;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    void View_VectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        if (_resetPending)
        {
            return;
        }

        if (_schedule is not null)
        {
            _resetPending = true;
            if (_schedule(Reset))
            {
                return;
            }
            _resetPending = false;
        }

        Reset();
    }

    void Reset()
    {
        _resetPending = false;
        _snapshot.Clear();
        _snapshot.AddRange(_view);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    // Read from the snapshot, not the view, so what a listener reads always agrees with the last event it
    // was given, including while a coalesced Reset is still pending.
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
