using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.WinUI.Collections;
using Kronos.Collections;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// The wrapper that makes the games grid populate on Uno. See NotifyingCollectionView's remarks: Uno's
/// grouped CollectionViewSource only hears INotifyCollectionChanged, and AdvancedCollectionView only raises
/// VectorChanged, so without this the Games page stayed empty.
/// </summary>
public class NotifyingCollectionViewTests
{
    static (ObservableCollection<string> source, AdvancedCollectionView view, NotifyingCollectionView wrapper, List<NotifyCollectionChangedEventArgs> events) Create(params string[] initial)
    {
        var source = new ObservableCollection<string>(initial);
        var view = new AdvancedCollectionView(source, true);
        view.SortDescriptions.Add(new SortDescription(SortDirection.Ascending));
        var wrapper = new NotifyingCollectionView(view);
        var events = new List<NotifyCollectionChangedEventArgs>();
        wrapper.CollectionChanged += (_, e) => events.Add(e);
        return (source, view, wrapper, events);
    }

    static List<object?> Contents(NotifyingCollectionView wrapper) => wrapper.Cast<object?>().ToList();

    [Fact]
    public void StartsWithTheViewsCurrentContents()
    {
        var (_, view, wrapper, _) = Create("b", "a");

        Assert.Equal(view.Cast<object>().ToList(), Contents(wrapper));
        Assert.Equal(new object[] { "a", "b" }, Contents(wrapper));
    }

    [Fact]
    public void AnAddedItemIsAnnouncedAsAnAddAtItsSortedPosition()
    {
        // The case that left the grid empty: games arriving after the grid was bound.
        var (source, _, wrapper, events) = Create("a", "c");

        source.Add("b");

        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal("b", change.NewItems![0]);
        Assert.Equal(new object[] { "a", "b", "c" }, Contents(wrapper));
    }

    [Fact]
    public void ARemovedItemIsAnnouncedWithTheItemThatWasRemoved()
    {
        // The view has already dropped it by the time VectorChanged fires, which is why the snapshot exists.
        var (source, _, wrapper, events) = Create("a", "b", "c");

        source.Remove("b");

        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action);
        Assert.Equal(1, change.OldStartingIndex);
        Assert.Equal("b", change.OldItems![0]);
        Assert.Equal(new object[] { "a", "c" }, Contents(wrapper));
    }

    [Fact]
    public void FilteringIsFollowed()
    {
        var (_, view, wrapper, events) = Create("apple", "banana", "avocado");

        view.Filter = item => ((string)item).StartsWith('a');

        Assert.NotEmpty(events);
        Assert.Equal(new object[] { "apple", "avocado" }, Contents(wrapper));
        Assert.Equal(view.Cast<object>().ToList(), Contents(wrapper));
    }

    [Fact]
    public void WhatAListenerReadsAgreesWithTheEventsItWasGiven()
    {
        var (source, view, wrapper, events) = Create();

        foreach (var title in new[] { "Portal", "Alan Wake 2", "Cyberpunk 2077", "Hellraiser" })
        {
            source.Add(title);
        }
        source.Remove("Portal");

        // Replaying the events onto an empty list must give exactly what the wrapper and the view hold.
        var replayed = new List<object?>();
        foreach (var e in events)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    replayed.Insert(e.NewStartingIndex, e.NewItems![0]);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    replayed.RemoveAt(e.OldStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    replayed[e.NewStartingIndex] = e.NewItems![0];
                    break;
                case NotifyCollectionChangedAction.Reset:
                    replayed = Contents(wrapper);
                    break;
            }
        }

        Assert.Equal(Contents(wrapper), replayed);
        Assert.Equal(view.Cast<object>().ToList(), replayed);
    }

    [Fact]
    public void ItIsReadOnly()
    {
        var (_, _, wrapper, _) = Create("a");

        Assert.True(wrapper.IsReadOnly);
        Assert.Throws<System.NotSupportedException>(() => wrapper.Add("b"));
    }
}
