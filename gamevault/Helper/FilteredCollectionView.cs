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
    /// Setting <see cref="Filter"/> re-evaluates the view. It is an IList so that Avalonia's ItemsControl tracks
    /// its changes (a plain IEnumerable is copied once).
    /// </summary>
    public class FilteredCollectionView<T> : ReadOnlyObservableCollection<T>
    {
        private readonly ObservableCollection<T> source;
        private readonly ObservableCollection<T> view;
        private Predicate<object>? filter;

        public FilteredCollectionView(ObservableCollection<T> source) : this(source, new ObservableCollection<T>())
        {
        }

        private FilteredCollectionView(ObservableCollection<T> source, ObservableCollection<T> view) : base(view)
        {
            this.source = source;
            this.view = view;
            source.CollectionChanged += (_, _) => Refresh();
            Refresh();
        }

        public Predicate<object>? Filter
        {
            get => filter;
            set { filter = value; Refresh(); }
        }

        public void Refresh()
        {
            var items = source.Where(i => filter == null || filter(i!)).ToList();
            if (items.SequenceEqual(view))
                return;
            view.Clear();
            foreach (T item in items)
                view.Add(item);
        }
    }
}
