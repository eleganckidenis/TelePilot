using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TelePilot.App.Collections;

/// <summary>
/// An observable collection that can replace its contents with a single reset notification.
/// </summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Raised immediately before a non-no-op replacement transaction mutates the collection.
    /// </summary>
    public event EventHandler? ReplacementStarting;

    /// <summary>
    /// Raised after the replacement reset notification has been delivered.
    /// </summary>
    public event EventHandler? ReplacementCompleted;

    /// <summary>
    /// Replaces all items and raises at most one collection reset notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] replacement = [.. items];

        if (HasSameItems(replacement))
        {
            return;
        }

        CheckReentrancy();
        ReplacementStarting?.Invoke(this, EventArgs.Empty);
        Items.Clear();
        foreach (T item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        ReplacementCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool HasSameItems(T[] replacement)
    {
        if (Items.Count != replacement.Length)
        {
            return false;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < replacement.Length; index++)
        {
            if (!comparer.Equals(Items[index], replacement[index]))
            {
                return false;
            }
        }

        return true;
    }
}
