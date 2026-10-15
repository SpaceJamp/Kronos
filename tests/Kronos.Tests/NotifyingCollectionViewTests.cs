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
    public void AnAddedItemIsAnnouncedAsAReset()
    {
        // The case that left the grid empty: games arriving after the grid was bound. Announced as a
        // Reset, not an Add, because Uno's grouped grid misplaces incremental changes (see the remarks).
        var (source, _, wrapper, events) = Create("a", "c");

        source.Add("b");

        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal(new object[] { "a", "b", "c" }, Contents(wrapper));
    }

    [Fact]
    public void ARemovedItemIsAnnouncedAsAReset()
    {
        var (source, _, wrapper, events) = Create("a", "b", "c");

        source.Remove("b");

        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal(new object[] { "a", "c" }, Contents(wrapper));
    }

    [Fact]
    public void WithASchedulerABurstOfChangesBecomesOneReset()
    {
        // Loading a library changes every game several times. Each Reset rebuilds the group's
        // containers, so a burst must collapse into one Reset that runs after it.
        var source = new ObservableCollection<string>();
        var view = new AdvancedCollectionView(source, true);
        var queued = new List<System.Action>();
        var wrapper = new NotifyingCollectionView(view, action => { queued.Add(action); return true; });
        var events = new List<NotifyCollectionChangedEventArgs>();
        wrapper.CollectionChanged += (_, e) => events.Add(e);

        source.Add("a");
        source.Add("b");
        source.Remove("a");
        source.Add("c");

        Assert.Empty(events);
        Assert.Empty(Contents(wrapper)); // still agrees with the last event a listener was given
        var reset = Assert.Single(queued);

        reset();

        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(events).Action);
        Assert.Equal(new object[] { "b", "c" }, Contents(wrapper));

        // The next change schedules a fresh Reset.
        source.Add("d");
        Assert.Equal(2, queued.Count);
    }

    [Fact]
    public void WhenTheSchedulerRefusesItResetsImmediately()
    {
        var source = new ObservableCollection<string>();
        var view = new AdvancedCollectionView(source, true);
        var wrapper = new NotifyingCollectionView(view, _ => false);
        var events = new List<NotifyCollectionChangedEventArgs>();
        wrapper.CollectionChanged += (_, e) => events.Add(e);

        source.Add("a");
        source.Add("b");

        Assert.Equal(2, events.Count);
        Assert.Equal(new object[] { "a", "b" }, Contents(wrapper));
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

    sealed class FakeGame : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        string _title;
        bool _visible = true;
        public FakeGame(string title) => _title = title;
        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }
        public override string ToString() => Title;
    }

    static List<object?> Replay(IEnumerable<NotifyCollectionChangedEventArgs> events, NotifyingCollectionView wrapper)
    {
        var replayed = new List<object?>();
        foreach (var e in events)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add: replayed.Insert(e.NewStartingIndex, e.NewItems![0]); break;
                case NotifyCollectionChangedAction.Remove: replayed.RemoveAt(e.OldStartingIndex); break;
                case NotifyCollectionChangedAction.Replace: replayed[e.NewStartingIndex] = e.NewItems![0]; break;
                case NotifyCollectionChangedAction.Reset: replayed = Contents(wrapper); break;
            }
        }
        return replayed;
    }

    [Fact]
    public void ItemsThatChangeAfterTheyAreAddedStayInStepWithTheView()
    {
        // The Games page showed one game's tile several times over and lost others. Games are added and
        // then processed: their Title (the sort key) and the properties the filter observes change after
        // they are already in the view, so AdvancedCollectionView moves, hides and re-shows them. A
        // listener replaying the wrapper's events has to end up with exactly the view's contents.
        var source = new ObservableCollection<FakeGame>();
        var view = new AdvancedCollectionView(source, true);
        view.Filter = item => ((FakeGame)item).Visible;
        view.ObserveFilterProperty(nameof(FakeGame.Visible));
        view.SortDescriptions.Add(new SortDescription(nameof(FakeGame.Title), SortDirection.Ascending));
        var wrapper = new NotifyingCollectionView(view);
        var events = new List<NotifyCollectionChangedEventArgs>();
        wrapper.CollectionChanged += (_, e) => events.Add(e);

        var games = new[] { "", "", "", "", "" }.Select(t => new FakeGame(t)).ToList();
        foreach (var g in games) source.Add(g);

        games[0].Title = "Wallpaper Engine";
        games[1].Title = "Call of Duty";
        games[2].Title = "Portal with RTX";
        games[3].Title = "Escape from Playtime";
        games[4].Title = "Steamworks Common Redistributables";
        games[4].Visible = false;
        games[2].Visible = false;
        games[2].Visible = true;
        games[1].Title = "Call of Duty®";

        var expected = view.Cast<object>().ToList();
        Assert.Equal(expected, Contents(wrapper));
        Assert.Equal(expected, Replay(events, wrapper));
        Assert.Equal(expected.Count, Replay(events, wrapper).Distinct().Count());
    }

    [Fact]
    public void ItIsReadOnly()
    {
        var (_, _, wrapper, _) = Create("a");

        Assert.True(wrapper.IsReadOnly);
        Assert.Throws<System.NotSupportedException>(() => wrapper.Add("b"));
    }
}
