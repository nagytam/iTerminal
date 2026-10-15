using iConsole.Data;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace iConsole.Model;

public interface IListBoxModel<T>
{
    ConcurrentObservableCollection<T> Items { get; set; }
    IReadOnlyList<IListItem<T>> ListItems { get; }
    bool HasPendingUpdate { get; }
    Task? UpdateTask { get; }
    Func<IEnumerable<T>, CancellationToken, IEnumerable<T>> FilterAndSortItems { get; set; }
    Func<IListItem<T>, CancellationToken, bool> Filter { get; set; }
    Func<T, CancellationToken, IListItem<T>> TransformItem { get; set; }
    Func<T, CancellationToken, bool> IsItemEnabled { get; set; }
    IListItem<T>? SelectedItem { get; }
    void UpdateListItems();

    /// <summary>
    /// Marks the projected list stale although <see cref="Items"/> did not change — after the
    /// <see cref="Filter"/>, <see cref="TransformItem"/> or sort changed what it would produce.
    /// <see cref="UpdateListItems"/> only re-projects when something is pending, and it only ever
    /// became pending from a change to <see cref="Items"/>, so a new filter was never applied.
    /// </summary>
    void Invalidate();
}