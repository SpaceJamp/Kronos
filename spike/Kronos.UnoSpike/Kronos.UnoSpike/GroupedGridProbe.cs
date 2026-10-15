using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Collections;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Kronos.UnoSpike;

/// <summary>
/// Phase 3: why is the Kronos games grid empty on Uno?
/// </summary>
/// <remarks>
/// GameManager builds the grid's source as a grouped CollectionViewSource (IsSourceGrouped, ItemsPath
/// "Games") over a list of GameGroup, where each GameGroup.Games is a CommunityToolkit AdvancedCollectionView
/// with a filter and a sort over one shared ObservableCollection - and the games are added after the grid
/// is bound. Each variant here removes one of those layers, so the first variant that works names the
/// layer that does not.
/// </remarks>
public sealed class GroupedGridProbe
{
    public sealed class Group
    {
        public string Name { get; init; } = "";
        public object? Games { get; init; }
    }

    readonly Action<string, bool, string> _note;

    public GroupedGridProbe(Action<string, bool, string> note) => _note = note;

    public async Task RunAsync(Panel host)
    {
        await Variant(host, "ungrouped GridView, ObservableCollection", grouped: false, useAcv: false, addLate: false);
        await Variant(host, "grouped CVS, ObservableCollection groups", grouped: true, useAcv: false, addLate: false);
        await Variant(host, "grouped CVS, ObservableCollection groups, items added after binding", grouped: true, useAcv: false, addLate: true);
        await Variant(host, "ungrouped GridView over AdvancedCollectionView", grouped: false, useAcv: true, addLate: false);
        await Variant(host, "grouped CVS, AdvancedCollectionView groups", grouped: true, useAcv: true, addLate: false);
        await Variant(host, "grouped CVS, AdvancedCollectionView groups, items added after binding (Kronos)", grouped: true, useAcv: true, addLate: true);
        await Variant(host, "ungrouped GridView over AdvancedCollectionView, items added after binding", grouped: false, useAcv: true, addLate: true);
        await Variant(host, "grouped CVS, AdvancedCollectionView groups wrapped to raise CollectionChanged, added late", grouped: true, useAcv: true, addLate: true, wrap: true);

        var acv = new AdvancedCollectionView(new ObservableCollection<string>(), true);
        _note("AdvancedCollectionView interfaces", true, string.Join(", ", acv.GetType().GetInterfaces().Select(i => i.Name)));
    }

    async Task Variant(Panel host, string name, bool grouped, bool useAcv, bool addLate, bool wrap = false)
    {
        try
        {
            var all = new ObservableCollection<string>();
            if (addLate == false)
            {
                foreach (var title in Titles) all.Add(title);
            }

            object MakeView()
            {
                if (useAcv == false) return all;
                var view = new AdvancedCollectionView(all, true);
                view.Filter = item => item is string s && s.Length > 0;
                view.SortDescriptions.Add(new SortDescription(SortDirection.Ascending));
                return wrap ? new NotifyingView(view) : view;
            }

            var grid = new GridView { Width = 600, Height = 400, SelectionMode = ListViewSelectionMode.None };

            if (grouped)
            {
                var groups = new ObservableCollection<Group>
                {
                    new() { Name = "Favourites", Games = MakeView() },
                    new() { Name = "Steam", Games = MakeView() },
                };
                var cvs = new CollectionViewSource { IsSourceGrouped = true, Source = groups, ItemsPath = new PropertyPath("Games") };
                grid.ItemsSource = cvs.View;
                grid.GroupStyle.Add(new GroupStyle { HidesIfEmpty = true });
            }
            else
            {
                grid.ItemsSource = MakeView();
            }

            host.Children.Add(grid);
            await Task.Delay(300);

            if (addLate)
            {
                foreach (var title in Titles) all.Add(title);
            }

            await Task.Delay(700);
            grid.UpdateLayout();

            var containers = CountDescendants<GridViewItem>(grid);
            var items = grid.Items.Count;
            var expected = grouped ? Titles.Length * 2 : Titles.Length;
            _note($"GridView: {name}", containers > 0 && items == expected, $"items={items} (expected {expected}), realized containers={containers}");

            host.Children.Remove(grid);
        }
        catch (Exception err)
        {
            _note($"GridView: {name}", false, $"{err.GetType().Name}: {err.Message}");
        }
    }

    /// <summary>
    /// The candidate fix: the same AdvancedCollectionView, re-announced as INotifyCollectionChanged.
    /// </summary>
    public sealed class NotifyingView : System.Collections.IList, System.Collections.Specialized.INotifyCollectionChanged
    {
        readonly AdvancedCollectionView _view;
        public NotifyingView(AdvancedCollectionView view)
        {
            _view = view;
            _view.VectorChanged += (_, _) => CollectionChanged?.Invoke(this,
                new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
        public event System.Collections.Specialized.NotifyCollectionChangedEventHandler? CollectionChanged;
        public object? this[int index] { get => _view[index]; set => throw new NotSupportedException(); }
        public bool IsFixedSize => false;
        public bool IsReadOnly => true;
        public int Count => _view.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(object? value) => _view.Contains(value!);
        public void CopyTo(Array array, int index) { for (var i = 0; i < _view.Count; i++) array.SetValue(_view[i], index + i); }
        public System.Collections.IEnumerator GetEnumerator() => _view.GetEnumerator();
        public int IndexOf(object? value) => _view.IndexOf(value!);
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    static readonly string[] Titles = { "Cyberpunk 2077", "Alan Wake 2", "Hellraiser", "Injustice 2", "Call of Duty HQ" };

    static int CountDescendants<T>(DependencyObject parent)
    {
        var count = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T) count++;
            count += CountDescendants<T>(child);
        }
        return count;
    }
}
