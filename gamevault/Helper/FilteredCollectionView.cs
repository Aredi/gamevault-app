using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// Minimal stand-in for WPF's CollectionViewSource.GetDefaultView: a read-only, filtered live view of a collection.
    /// Setting <see cref="Filter"/> re-evaluates the view.
    /// </summary>
    public class FilteredCollectionView<T> : IEnumerable<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private readonly ObservableCollection<T> source;
        private readonly ObservableCollection<T> view = new ObservableCollection<T>();
        private Predicate<object>? filter;

        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => view.CollectionChanged += value;
            remove => view.CollectionChanged -= value;
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        public FilteredCollectionView(ObservableCollection<T> source)
        {
            this.source = source;
            source.CollectionChanged += (_, _) => Refresh();
            Refresh();
        }

        public Predicate<object>? Filter
        {
            get => filter;
            set { filter = value; Refresh(); }
        }

        public int Count => view.Count;

        public void Refresh()
        {
            var items = source.Where(i => filter == null || filter(i!)).ToList();
            if (items.SequenceEqual(view))
                return;
            view.Clear();
            foreach (T item in items)
                view.Add(item);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        public IEnumerator<T> GetEnumerator() => view.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => view.GetEnumerator();
    }
}
