using System.Data.Common;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// The breakpoints the user has set, by file and line (1-based), kept in the database so they survive a restart. The debugger
/// reads them when a session starts and sends each file's lines to the adapter; toggling one while a session runs sends the
/// file again.
/// <para>
/// Reads come from memory. A change is applied in memory at once and its row is written by a background task, in the order
/// the changes were made, so a key handler never waits on the database. The rows are read once, in the background, when the
/// store is created; a change made before that read finishes is kept over the row it would have replaced.
/// </para>
/// </summary>
public sealed class BreakpointStore
{
    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, SortedSet<int>> _byFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The breakpoints the user changed since the store was created, as <c>path|line</c>. A row read later must not undo them.</summary>
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();

    /// <summary>The last row write queued. Every write waits for the one before it, so the database ends with the last change.</summary>
    private Task _writes = Task.CompletedTask;

    public BreakpointStore(IDbContextFactory<iTerminalDbContext> dbContextFactory, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        Loaded = Task.Run(LoadAsync);
    }

    /// <summary>Completes when the saved breakpoints have been read into memory (or the read has failed and been logged).</summary>
    public Task Loaded { get; }

    /// <summary>Completes when the row writes queued so far have finished. Nothing waits on it in the app; a test does.</summary>
    public Task PendingWrites
    {
        get
        {
            lock (_lock) return _writes;
        }
    }

    /// <summary>Sets the breakpoint at the line if it is not set, and removes it if it is. Returns true when one now exists.</summary>
    public bool Toggle(string path, int line)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        lock (_lock)
        {
            if (!_byFile.TryGetValue(path, out SortedSet<int>? lines))
            {
                lines = [];
                _byFile[path] = lines;
            }

            bool set = !lines.Remove(line);
            if (set)
            {
                lines.Add(line);
            }
            else if (lines.Count == 0)
            {
                _byFile.Remove(path);
            }

            _changed.Add(KeyOf(path, line));
            QueueWrite(path, line, set);
            return set;
        }
    }

    public bool Contains(string path, int line)
    {
        lock (_lock)
        {
            return _byFile.TryGetValue(path, out SortedSet<int>? lines) && lines.Contains(line);
        }
    }

    /// <summary>Every breakpoint, ordered by file then line.</summary>
    public IReadOnlyList<(string Path, int Line)> All()
    {
        lock (_lock)
        {
            return _byFile.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(line => (pair.Key, line)))
                .ToList();
        }
    }

    /// <summary>The lines of one file's breakpoints, in order (empty when there are none).</summary>
    public IReadOnlyList<int> LinesIn(string path)
    {
        lock (_lock)
        {
            return _byFile.TryGetValue(path, out SortedSet<int>? lines) ? [.. lines] : [];
        }
    }

    /// <summary>Every file that has a breakpoint.</summary>
    public IReadOnlyList<string> Files()
    {
        lock (_lock)
        {
            return [.. _byFile.Keys];
        }
    }

    /// <summary>Removes every breakpoint, in memory and in the database.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach ((string path, SortedSet<int> lines) in _byFile)
            {
                foreach (int line in lines)
                {
                    _changed.Add(KeyOf(path, line));
                }
            }

            _byFile.Clear();
            _writes = _writes.ContinueWith(_ => DeleteAll(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static string KeyOf(string path, int line) => $"{path}|{line}";

    /// <summary>Queues the row write for a change, behind the writes already queued. The caller holds the lock.</summary>
    private void QueueWrite(string path, int line, bool set) =>
        _writes = _writes.ContinueWith(_ => Write(path, line, set), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    private void Write(string path, int line, bool set)
    {
        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            if (set)
            {
                if (context.DevelopmentBreakpoints.Find(path, line) is null)
                {
                    context.DevelopmentBreakpoints.Add(new DevelopmentBreakpointEntry { Path = path, Line = line });
                    context.SaveChanges();
                }
            }
            else
            {
                context.DevelopmentBreakpoints.Where(entry => entry.Path == path && entry.Line == line).ExecuteDelete();
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException or InvalidOperationException or IOException)
        {
            _logger?.Warning(exception, "Could not save the breakpoint at {Path}:{Line}", path, line);
        }
    }

    private void DeleteAll()
    {
        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.DevelopmentBreakpoints.ExecuteDelete();
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException or InvalidOperationException or IOException)
        {
            _logger?.Warning(exception, "Could not clear the saved breakpoints");
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await using iTerminalDbContext context = await _dbContextFactory.CreateDbContextAsync();
            List<DevelopmentBreakpointEntry> rows = await context.DevelopmentBreakpoints.AsNoTracking().ToListAsync();
            lock (_lock)
            {
                foreach (DevelopmentBreakpointEntry row in rows)
                {
                    if (row.Line < 1 || _changed.Contains(KeyOf(row.Path, row.Line))) continue;
                    if (!_byFile.TryGetValue(row.Path, out SortedSet<int>? lines))
                    {
                        lines = [];
                        _byFile[row.Path] = lines;
                    }

                    lines.Add(row.Line);
                }
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException or InvalidOperationException or IOException)
        {
            _logger?.Warning(exception, "Could not read the saved breakpoints");
        }
    }
}
