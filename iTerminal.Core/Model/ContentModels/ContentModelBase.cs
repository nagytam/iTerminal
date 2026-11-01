using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Model.ContentModels;

public abstract class ContentModelBase : ScrollableModel, IContentModel, IMarkableContentModel, ISortableContentModel
{
    private readonly IFrequentExecutionGuardService _frequentExecutionGuardService;

    /// <summary>
    /// The snapshot <see cref="FilteredEntries"/> last handed out, with the filter and the entries
    /// version it was taken under. One immutable record rather than three fields, so a reader on one
    /// thread can never see the list of one snapshot with the stamp of another.
    /// </summary>
    private sealed record FilteredSnapshot(IList<IEntry> Entries, string? Filter, int Version, PanelSort Sort, PanelViewOptions Options, Func<IEntry, bool>? EntryFilter);

    private FilteredSnapshot? _filteredSnapshot;

    /// <summary>Moves on with every change to <see cref="Entries"/>.</summary>
    private int _entriesVersion;
    private string? _focusedEntryUrl;
    private string? _filterRestoreEntryUrl;

    /// <summary>
    /// Guards the cursor-restore state — the remembered row, the pending restore and the cursor
    /// they move — which is written from two threads: the key handler (navigating, restoring a
    /// session, filtering) and the worker that lands a listing. Unguarded, a listing landing while a
    /// restore was being applied remembered the cursor in the instant between the restore clearing
    /// its flag and moving the cursor, and then put the cursor back on that row: "..".
    /// </summary>
    private readonly Lock _navigationLock = new();

    /// <summary>
    /// True while the listing on screen is a streamed batch of a folder still being enumerated. A
    /// restore cannot conclude that its row is gone from such a listing — it may simply not have
    /// arrived — so it waits for the complete one instead of settling for its fallback index.
    /// </summary>
    private bool _listingIncomplete;

    /// <summary>
    /// A complete listing read from the folder itself has landed for the URL being shown. A copy
    /// served from a cache is older than that by definition and must not replace it.
    /// </summary>
    private bool _freshListingLanded;

    private string? _pendingEntryUrl;
    private long _pendingCursorPositionY;
    private long _pendingScrollPositionY;
    private bool _hasPendingNavigation;

    public IContentBrowser ContentBrowser { get; set; } = null!;
    public virtual IList<IEntry> FilteredEntries
    {
        get
        {
            try
            {
                // The version is read before the snapshot is taken. Invalidating by clearing the
                // cache — which is what this used to do — lost a race: a reader took a snapshot, a
                // listing landed on a worker thread and cleared the cache, and the reader then stored
                // the snapshot it had taken *before* the listing. That stale list was served until the
                // next change, and a panel restoring its cursor read one row where there were several
                // and parked the restore for a listing that had already arrived. Stamped with the
                // version it was taken under, a late store is simply never matched.
                var version = Volatile.Read(ref _entriesVersion);
                var filter = Filter;
                var sort = CanSort ? _sort : PanelSort.AsListed;
                var options = CanSort ? _viewOptions : PanelViewOptions.Default;
                var entryFilter = _entryFilter;
                var cached = _filteredSnapshot;
                if (cached is not null && cached.Version == version && cached.Filter == filter && cached.Sort == sort && cached.Options == options
                    && ReferenceEquals(cached.EntryFilter, entryFilter))
                {
                    return cached.Entries;
                }

                // Sorted here, in the one list the cursor, the marks and the drawing all index —
                // so every drive sorts alike, and none of them has to know how.
                IList<IEntry> entries = sort.Apply(Entries.AsSnapshot(), options);
                if (entryFilter is not null)
                {
                    entries = entries.Where(entryFilter).ToList();
                }

                _filteredSnapshot = new FilteredSnapshot(entries, filter, version, sort, options, entryFilter);
                return entries;
            }
            catch
            {
                return [];
            }
        }
    }

    #region Sort
    private PanelSort _sort = PanelSort.AsListed;

    public virtual bool CanSort => true;

    public PanelSort Sort => CanSort ? _sort : PanelSort.AsListed;

    private PanelViewOptions _viewOptions = PanelViewOptions.Default;

    public PanelViewOptions ViewOptions => CanSort ? _viewOptions : PanelViewOptions.Default;

    public void SetViewOptions(PanelViewOptions options)
    {
        if (CanSort && _viewOptions != options)
        {
            Reorder(() => _viewOptions = options);
        }
    }

    public void SetSort(PanelSort sort)
    {
        if (CanSort && _sort != sort)
        {
            Reorder(() => _sort = sort);
        }
    }

    /// <summary>Changes what orders or filters the rows, and puts the cursor back on the entry it was on.</summary>
    private void Reorder(Action change)
    {
        lock (_navigationLock)
        {
            // The cursor stays on the entry, not on the row number: the entry is what the user
            // was looking at, and it is somewhere else in the new order.
            // A model that has listed nothing yet has no cursor to keep: it is only being told
            // the order its first listing will be in.
            var hasRows = Entries.Count > 0;
            var focusedUrl = hasRows ? GetFocusedEntry()?.Url.FullUrl : null;
            change();
            if (!hasRows)
            {
                return;
            }

            if (focusedUrl is not null)
            {
                var entries = FilteredEntries;
                for (var index = 0; index < entries.Count; index++)
                {
                    if (entries[index].Url.FullUrl == focusedUrl)
                    {
                        CursorPosition.Y = index;
                        break;
                    }
                }

                // The entry itself may be gone — hidden files were just switched off with the
                // cursor on one: the cursor stays on a row that exists.
                CursorPosition.Y = Math.Clamp(CursorPosition.Y, 0, Math.Max(0, entries.Count - 1));
            }

            ScrollToVisible();
        }

        Update();
    }
    #endregion

    private Func<IEntry, bool>? _entryFilter;
    private string? _entryFilterDescription;

    /// <summary>What the entries are narrowed to, as a phrase for the footer; null when nothing narrows them.</summary>
    public string? EntryFilterDescription => _entryFilterDescription;

    /// <summary>
    /// Shows only the entries <paramref name="predicate"/> accepts; null shows them all again. The cursor stays on its entry
    /// when it is still shown, and moves to the nearest one when it is not.
    /// </summary>
    public void SetEntryFilter(Func<IEntry, bool>? predicate, string? description)
    {
        Reorder(() =>
        {
            _entryFilter = predicate;
            _entryFilterDescription = predicate is null ? null : description;
        });
    }

    /// <summary>Applies the entry filter again, for a change in what it reads (a label): the list is rebuilt, the cursor kept on its entry.</summary>
    public void RefreshEntryFilter() => Reorder(() => Interlocked.Increment(ref _entriesVersion));

    private string? _filter;
    public virtual string? Filter
    {
        get => _filter;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                value = null;
            }

            if (_filter != value)
            {
                _filter = value;
            }
        }
    }

    #region View
    public long Length => FilteredEntries.Count;
    #endregion

    public virtual string Name { get; private set; }
    public virtual Url Url { get; protected set; } = null!;
    public ConcurrentObservableCollection<IEntry> Entries { get; private set; } = [];
    public abstract bool IsReadOnly { get; }

    public ContentModelBase(
        string name,
        IFrequentExecutionGuardService frequentExecutionGuardService
    )
    {
        _frequentExecutionGuardService = frequentExecutionGuardService;
        Name = name;
        Entries.CollectionChanged += Entries_CollectionChanged;
    }

    private void Entries_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Retires every FilteredEntries snapshot taken before this change.
        Interlocked.Increment(ref _entriesVersion);

        // Fire asynchronously so this handler can never re-enter the collection lock
        // on the same thread (defence-in-depth alongside ConcurrentObservableCollection's
        // lock-release-before-notify refactor).
        Task.Run(() => OnPropertyChanged(nameof(Entries)));
    }

    public abstract string GetFooter();

    /// <summary>
    /// " 5 entries " — or " 1 entry ", and the filter when one is set — framed by spaces like every
    /// other title drawn into a border. It used to put the border character itself between the
    /// number and the word, so the bottom edge of every panel read "══5═entries══", which looks like
    /// a drawing glitch rather than a count.
    /// </summary>
    protected string EntryCountFooter()
    {
        var count = FilteredEntries.Count;
        var footer = $" {count} {(count == 1 ? "entry" : "entries")} ";
        var marked = GetMarkedEntries();
        if (marked.Count > 0)
        {
            footer += $"{marked.Count} marked";
            var bytes = marked.Where(e => e.Url.PathType == PathType.File).Sum(e => Math.Max(0, e.Url.Length));
            footer += bytes > 0 ? $", {bytes.ToFileSize().Trim()} " : " ";
        }
        if (Filter is not null)
        {
            footer += $"Filter: {Filter} ";
        }
        if (EntryFilterDescription is not null)
        {
            footer += $"{EntryFilterDescription} ";
        }
        if (!Sort.IsAsListed)
        {
            footer += $"Sort: {Sort.Describe()} ";
        }
        return footer;
    }

    #region Marks
    private readonly Lock _marksLock = new();
    private readonly HashSet<string> _marks = new(StringComparer.Ordinal);

    /// <summary>The folder the marks were made in; marks found under any other URL are dropped.</summary>
    private string? _marksFolder;

    public virtual bool CanMark => true;

    public int MarkedCount => GetMarkedEntries().Count;

    private static string MarkKey(IEntry entry) => (entry.Url?.FullUrl ?? string.Empty).TrimEnd('/', '\\');

    private static bool IsMarkableEntry(IEntry? entry) =>
        entry?.Url is not null && !string.Equals(entry.DisplayName?.Trim(), "..", StringComparison.Ordinal);

    /// <summary>
    /// Drops the marks when the panel has moved on. Navigation clears them in
    /// <see cref="ResetNavigation"/>; this is the backstop for a drive whose <c>SetUrl</c> does not
    /// go through it. Call with <see cref="_marksLock"/> held.
    /// </summary>
    private void DropMarksFromAnotherFolder()
    {
        var folder = Url?.FullUrl;
        if (!string.Equals(_marksFolder, folder, StringComparison.Ordinal))
        {
            _marks.Clear();
            _marksFolder = folder;
        }
    }

    public bool IsMarked(IEntry? entry)
    {
        if (!CanMark || !IsMarkableEntry(entry))
        {
            return false;
        }

        lock (_marksLock)
        {
            DropMarksFromAnotherFolder();
            return _marks.Count > 0 && _marks.Contains(MarkKey(entry!));
        }
    }

    public bool SetMark(IEntry? entry, bool marked)
    {
        if (!CanMark || !IsMarkableEntry(entry))
        {
            return false;
        }

        lock (_marksLock)
        {
            DropMarksFromAnotherFolder();
            return marked ? _marks.Add(MarkKey(entry!)) : _marks.Remove(MarkKey(entry!));
        }
    }

    public bool ToggleMark(IEntry? entry) => SetMark(entry, !IsMarked(entry));

    public int SetMarks(string patterns, bool marked, bool includeFolders)
    {
        var expressions = Services.Search.FileSearch.ParsePatterns(patterns);
        var changed = 0;
        foreach (var entry in FilteredEntries)
        {
            if ((includeFolders || entry.Url.PathType != PathType.Folder)
                && Services.Search.FileSearch.MatchesName(entry.DisplayName.Trim().TrimEnd('/', '\\'), expressions)
                && SetMark(entry, marked))
            {
                changed++;
            }
        }

        return changed;
    }

    public void InvertMarks(bool includeFolders)
    {
        foreach (var entry in FilteredEntries)
        {
            if (includeFolders || entry.Url.PathType != PathType.Folder)
            {
                ToggleMark(entry);
            }
        }
    }

    public void ClearMarks()
    {
        lock (_marksLock)
        {
            _marks.Clear();
        }
    }

    public IReadOnlyList<IEntry> GetMarkedEntries()
    {
        if (!CanMark)
        {
            return [];
        }

        lock (_marksLock)
        {
            DropMarksFromAnotherFolder();
            if (_marks.Count == 0)
            {
                return [];
            }
        }

        // Read from the listing rather than from the set, so an entry gone since it was marked is
        // simply not reported, and the order is the order on screen.
        return [.. FilteredEntries.Where(IsMarked)];
    }
    #endregion
    public abstract string GetHeader(int width);
    public abstract void ReadUrl();
    public abstract void Refresh(bool forceRefresh = false);
    public abstract void SetUrl(Url path);

    public virtual IEntry? GetFocusedEntry()
    {
        IEntry? focusedEntry = null;
        try
        {
            var filteredEntries = FilteredEntries;
            if (CursorPosition is not null
                && CursorPosition.Y >= 0
                && CursorPosition.Y < filteredEntries.Count)
            {
                focusedEntry = filteredEntries[(int)CursorPosition.Y];
            }
        }
        catch
        {
        }
        return focusedEntry;
    }

    public virtual void RememberFocusedEntry()
    {
        lock (_navigationLock)
        {
            // While a restore is outstanding the cursor is wherever an unfinished listing left it —
            // the ".." row, in practice — and remembering that would only overwrite the row the
            // panel is on its way back to.
            if (_hasPendingNavigation)
            {
                return;
            }

            _focusedEntryUrl = GetFocusedEntry()?.Url.FullUrl;
        }
    }

    /// <summary>
    /// Lands a new listing: remembers the row under the cursor, swaps the entries, and puts the
    /// cursor back — on a pending restore when <paramref name="isComplete"/> and one is waiting,
    /// otherwise on the row it was on. One step under <see cref="_navigationLock"/>, so a restore
    /// asked for on the key handler cannot interleave with it; every producer of listings goes
    /// through here rather than calling the three parts itself.
    /// </summary>
    /// <param name="isComplete">
    /// True when <paramref name="entries"/> is the whole folder, false for a streamed batch. Only a
    /// complete listing can land a restore: a batch of the first 50 rows would clamp a cursor that
    /// belongs at row 300.
    /// </param>
    /// <param name="isFromCache">
    /// True when <paramref name="entries"/> was served from a cache rather than read from the folder.
    /// Two refresh tasks can be running for one URL (the navigation's and the panel's start-up), and
    /// the slower one's cache lookup can land after the other's fresh listing: the panel then showed
    /// the folder as it was last run — without the files added since — until some later refresh, and
    /// forever when that task was cancelled before it enumerated. A cached listing is therefore
    /// dropped once a fresh one has landed.
    /// </param>
    public void ReplaceEntriesAndRestoreFocus(IList<IEntry> entries, bool isComplete, bool isFromCache = false)
    {
        lock (_navigationLock)
        {
            if (isFromCache && _freshListingLanded)
            {
                return;
            }

            if (isComplete && !isFromCache)
            {
                _freshListingLanded = true;
            }

            var position = CursorPosition;
            var cursorBefore = CursorPosition.Y;
            RememberFocusedEntry();
            Entries.ReplaceAll(entries);
            _listingIncomplete = !isComplete;
            Update();

            // A restore asked for (a session, a folder left, a search hit) outranks the row the
            // cursor happens to be on, which while a folder is loading is "..", every time.
            if (isComplete && ApplyPendingNavigation())
            {
                return;
            }

            // A key that moved the cursor while the listing was being swapped wins over the row
            // remembered before it. The key handler cannot take this lock — it would hold it across
            // awaits — so this is checked instead: putting the cursor back on the remembered row
            // undid the move, and Down on a folder refreshing in the background landed on "..".
            var expected = Math.Min(cursorBefore, Math.Max(0, FilteredEntries.Count - 1));
            if (!ReferenceEquals(position, CursorPosition) || CursorPosition.Y != expected)
            {
                ScrollToVisible();
                return;
            }

            if (!RestoreFocusedEntry())
            {
                ScrollToVisible();
                return;
            }

            Update();
        }
    }

    public virtual void RememberFilterRestoreEntry()
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            _filterRestoreEntryUrl = GetFocusedEntry()?.Url.FullUrl;
        }
    }

    public virtual bool RestoreFocusedEntry()
    {
        return RestoreFocusedEntry(_focusedEntryUrl);
    }

    public virtual bool RestoreFilterRestoreEntry()
    {
        var restored = RestoreFocusedEntry(_filterRestoreEntryUrl);
        if (string.IsNullOrWhiteSpace(Filter))
        {
            _filterRestoreEntryUrl = null;
        }

        return restored;
    }

    private bool RestoreFocusedEntry(string? entryUrl)
    {
        if (string.IsNullOrWhiteSpace(entryUrl))
        {
            return false;
        }

        lock (_navigationLock)
        {
            var filteredEntries = FilteredEntries;
            for (var index = 0; index < filteredEntries.Count; index++)
            {
                if (string.Equals(filteredEntries[index].Url.FullUrl, entryUrl, StringComparison.Ordinal))
                {
                    CursorPosition.Y = index;
                    ScrollToVisible();
                    return true;
                }
            }

            return false;
        }
    }

    public virtual bool IsEntryMatch(IEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        return entry.Url.DisplayName.Contains(Filter, StringComparison.InvariantCultureIgnoreCase);
    }

    public virtual int GetFirstMatchingIndex()
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return 0;
        }

        var filteredEntries = FilteredEntries;
        for (var index = 0; index < filteredEntries.Count; index++)
        {
            if (IsEntryMatch(filteredEntries[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Puts the panel back where the last session left it: the row that was under the cursor, and
    /// the row that was at the top of the view.
    ///
    /// <para>The position cannot simply be assigned. A file panel enumerates its folder on a
    /// background task, so at the moment a session is restored the listing holds nothing but the
    /// <c>..</c> row, and <see cref="Update"/> clamps any cursor set against it straight back to
    /// zero. The restore is therefore held until the listing that can satisfy it arrives — which is
    /// immediately for a drive that lists synchronously or a folder served from cache, and a moment
    /// later for a cold folder.</para>
    ///
    /// <para><paramref name="focusedEntryUrl"/> is what the cursor is really restored to; the index
    /// is only the fallback for when that row is no longer there. A folder is not the same list it
    /// was when the app closed — a file may have been added above the one the cursor was on — and an
    /// index alone would then land on a different row than the one the user left.</para>
    /// </summary>
    public virtual void RestoreNavigation(string? focusedEntryUrl, long cursorPositionY, long scrollPositionY)
    {
        lock (_navigationLock)
        {
            _pendingEntryUrl = string.IsNullOrWhiteSpace(focusedEntryUrl) ? null : focusedEntryUrl;
            _pendingCursorPositionY = Math.Max(0, cursorPositionY);
            _pendingScrollPositionY = Math.Max(0, scrollPositionY);
            _hasPendingNavigation = true;

            // Anything beyond the ".." row means a listing is there — a synchronous drive, or a folder
            // that came out of the cache. One still being enumerated has only that row, or a streamed
            // batch, which ApplyPendingNavigation does not settle for when the row is not in it.
            if (FilteredEntries.Count > 1)
            {
                ApplyPendingNavigation();
            }
        }
    }

    /// <summary>
    /// Lands a pending restore on the listing as it now stands, and forgets it either way — a
    /// restore is for the listing the panel opened with, and must never fight the user for the
    /// cursor on a later refresh. Returns true when there was one to apply.
    /// </summary>
    public bool ApplyPendingNavigation()
    {
        lock (_navigationLock)
        {
            if (!_hasPendingNavigation)
            {
                return false;
            }

            IList<IEntry> entries = FilteredEntries;
            long cursor = IndexOfEntry(entries, _pendingEntryUrl);

            if (cursor < 0)
            {
                // The remembered row is not in this listing. That does not mean it is gone: two refresh
                // tasks can be replacing the same list at once — the navigation enqueues one and the
                // panel's own start-up enqueues another — so a listing read here can be one that is
                // still filling. A listing too short to hold the saved index is treated as exactly
                // that, and the restore waits for a longer one rather than collapsing onto row zero.
                if (_listingIncomplete || entries.Count <= _pendingCursorPositionY || entries.Count <= 1)
                {
                    return false;
                }

                cursor = _pendingCursorPositionY;
            }

            _hasPendingNavigation = false;
            _pendingEntryUrl = null;

            long maxIndex = Math.Max(0, entries.Count - 1);
            CursorPosition.Y = cursor.Range(0, maxIndex);
            ScrollPosition.Y = _pendingScrollPositionY.Range(0, maxIndex);

            // From here the ordinary machinery keeps the panel on this row: the next refresh remembers
            // it and restores it, the way it does for a row the user picked themselves.
            _focusedEntryUrl = GetFocusedEntry()?.Url.FullUrl;

            // Only corrects the saved offset when the cursor no longer falls inside it — which is
            // exactly what happens when the remembered row has moved.
            ScrollToVisible();
            Update();
            return true;
        }
    }

    private static int IndexOfEntry(IList<IEntry> entries, string? entryUrl)
    {
        if (string.IsNullOrEmpty(entryUrl))
        {
            return -1;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            if (IsSameLocation(entries[index].Url.FullUrl, entryUrl))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether two URLs name the same entry, ignoring a trailing separator: a folder is written both
    /// ways depending on who built the URL (the ".." row, <c>Url.ParentUrl</c>, a typed path).
    /// </summary>
    internal static bool IsSameLocation(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.Ordinal);

    public virtual void ResetNavigation()
    {
        lock (_navigationLock)
        {
            // Navigating somewhere else retires a restore that has not landed: it describes the folder
            // the panel opened with, not this one.
            _hasPendingNavigation = false;
            _pendingEntryUrl = null;
            _listingIncomplete = false;
            _freshListingLanded = false;

            // Marks describe rows of the folder being left; an operation must never act on rows the
            // user can no longer see. Every drive's SetUrl comes through here.
            ClearMarks();

            if (CursorPosition is null)
            {
                CursorPosition = new(0, 0);
            }
            else
            {
                CursorPosition.Y = 0;
            }
            ScrollPosition.Y = 0;
        }
    }

    public override void Update()
    {
        var filteredCount = FilteredEntries.Count;
        var maxIndex = Math.Max(0, filteredCount - 1);

        ScrollableSize.Height = filteredCount;
        ScrollPosition.Y = ScrollPosition.Y.Range(0, maxIndex);
        CursorPosition.Y = CursorPosition.Y.Range(0, maxIndex);
        _frequentExecutionGuardService.Execute(
            () =>
            {
                OnPropertyChanged(nameof(Entries));
                OnPropertyChanged(nameof(Filter));
            });
    }

    /// <summary>
    /// The Brief cell for an entry: the name alone, with the same filter-miss highlight as the full row.
    /// It calls the entry's brief formatting, not <see cref="FormatItemName"/>, so size and date do not return.
    /// </summary>
    public virtual ConxelString FormatBriefItemName(IEntry? entry, int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        if (entry is null)
        {
            var empty = new ConxelString();
            empty.Append(StringHelper.Repeat(' ', width), colorScheme.Foreground, colorScheme.Background);
            return empty;
        }

        var conxelString = entry.FormatBriefItemName(width, cursor, colorScheme, visualState);
        if (!cursor && !IsEntryMatch(entry))
        {
            for (var i = 0; i < conxelString.Count; i++)
            {
                var cell = conxelString.String[i];
                cell.BackgroundColor = Color.Blue;
                conxelString.String[i] = cell;
            }
        }

        return conxelString;
    }

    public virtual ConxelString FormatItemName(IEntry? entry, int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        var (nameLength, showDate, showTime) = EntryBase.ColumnLayout(width);

        ConxelString conxelString;
        if (entry is not null)
        {
            conxelString = entry.FormatItemName(width, cursor, colorScheme, visualState);

            if (!cursor && !IsEntryMatch(entry))
            {
                for (var i = 0; i < conxelString.Count; i++)
                {
                    var cell = conxelString.String[i];
                    cell.BackgroundColor = Color.Blue;
                    conxelString.String[i] = cell;
                }
            }
        }
        else
        {
            conxelString = new();
            var foregroundColor = colorScheme.Foreground;
            var backgroundColor = colorScheme.Background;
            var text = StringHelper.Repeat(' ', nameLength) + Border.VerticalSingle
                       + StringHelper.Repeat(' ', EntryBase.SizeLength)
                       + (showDate ? Border.VerticalSingle + StringHelper.Repeat(' ', EntryBase.DateLength) : string.Empty)
                       + (showTime ? Border.VerticalSingle + StringHelper.Repeat(' ', EntryBase.TimeLength) : string.Empty);
            conxelString.Append(text, foregroundColor, backgroundColor);
        }
        return conxelString;
    }

    public virtual void Delete(Url url, bool recursive, bool moveToRecycleBin)
    {
        throw new NotImplementedException();
    }

    public virtual Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        throw new NotImplementedException();
    }

    public virtual bool ExistsFile(Url url)
    {
        return false;
    }

    public virtual bool ExistsFolder(Url url)
    {
        return false;
    }

    public virtual Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        throw new NotImplementedException();
    }

    public virtual Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        throw new NotImplementedException();
    }

    public virtual bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        throw new NotImplementedException();
    }

    public virtual bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        throw new NotImplementedException();
    }

    public virtual Task CreateFolderAsync(Url url)
    {
        throw new NotImplementedException();
    }

    public virtual Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        throw new NotImplementedException();
    }

    public virtual string GetAvailableDiskSpace()
    {
        throw new NotImplementedException();
    }

    public virtual Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task RunTerminalCommandAsync(ITerminalModel terminalModel)
    {
        throw new NotImplementedException();
    }
}
