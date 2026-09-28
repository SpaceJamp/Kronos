using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Kronos.Collections;

public class RefreshableObservableCollection<T> : ObservableCollection<T>
{
    public RefreshableObservableCollection() : base() { }

    public void RaiseCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        OnCollectionChanged(e);
    }
}
