using System.Reflection;
using System.Text.Json;
using iConsole.Viewers;
using iTerminal.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace iTerminal.Core.Services.Plugins;

/// <summary>
/// Loads the viewer and editor plug-ins in the plug-in folder into the F3 and F4 registries, and unloads them again.
/// <para>
/// A plug-in is an assembly that holds public classes implementing <see cref="IFileViewer"/> or <see cref="IFileEditor"/>.
/// Each is created by the container (so it can take any registered service) and added to its registry. Unloading removes
/// those instances and releases the assembly's context; its memory goes when the garbage collector finds it unreferenced,
/// so a viewer dialog the plug-in opened keeps it alive until that dialog closes.
/// </para>
/// <para>
/// Loads and unloads run one at a time on a background task, because they read files and reflect over types. The state
/// changes on the calling thread at once, so a dialog shows "loading" straight away.
/// </para>
/// </summary>
public sealed class PluginManagerService : IPluginManagerService
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The plug-ins the user loaded, saved in the settings. Changed only by the user, so a plug-in that fails at start-up stays here.
    private readonly HashSet<string> _wanted = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly FileViewerRegistry _viewers;
    private readonly FileEditorRegistry _editors;
    private readonly ILogger _logger;
    private long _version;

    public PluginManagerService(
        IServiceProvider services,
        PluginLocation location,
        ISettingsService settings,
        FileViewerRegistry viewers,
        FileEditorRegistry editors,
        ILogger logger)
    {
        _services = services;
        _settings = settings;
        _viewers = viewers;
        _editors = editors;
        _logger = logger.ForContext<PluginManagerService>();
        Folder = location.Directory;
    }

    public string Folder { get; }

    public long Version => Interlocked.Read(ref _version);

    public IReadOnlyList<PluginInfo> Plugins
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values
                    .OrderBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new PluginInfo(entry.FileName, entry.State, entry.Viewers.Count, entry.Editors.Count, entry.Error))
                    .ToList();
            }
        }
    }

    public async Task<int> StartAsync()
    {
        IReadOnlyList<string> saved = ReadSaved();
        lock (_gate)
        {
            _wanted.Clear();
            foreach (string name in saved)
            {
                _wanted.Add(name);
            }
        }

        await RescanAsync();

        List<Entry> toLoad = [];
        lock (_gate)
        {
            foreach (string name in saved)
            {
                if (_entries.TryGetValue(name, out Entry? entry) && entry.State is PluginState.Unloaded or PluginState.Failed)
                {
                    entry.State = PluginState.Loading;
                    toLoad.Add(entry);
                }
            }

            if (toLoad.Count > 0)
            {
                Changed();
            }
        }

        if (toLoad.Count == 0)
        {
            return 0;
        }

        await Task.Run(async () =>
        {
            await _work.WaitAsync();
            try
            {
                foreach (Entry entry in toLoad)
                {
                    Load(entry);
                }
            }
            finally
            {
                _work.Release();
            }
        });

        lock (_gate)
        {
            return toLoad.Count(entry => entry.State == PluginState.Failed);
        }
    }

    public Task RescanAsync() => Task.Run(Scan);

    public Task SetLoadedAsync(string fileName, bool loaded)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(fileName, out Entry? found))
            {
                return Task.CompletedTask;
            }

            entry = found;
            bool canChange = loaded
                ? entry.State is PluginState.Unloaded or PluginState.Failed
                : entry.State == PluginState.Loaded;
            if (!canChange)
            {
                return Task.CompletedTask;
            }

            entry.State = loaded ? PluginState.Loading : PluginState.Unloading;
            entry.Error = null;
            if (loaded)
            {
                _wanted.Add(fileName);
            }
            else
            {
                _wanted.Remove(fileName);
            }

            Changed();
        }

        return Task.Run(async () =>
        {
            await _work.WaitAsync();
            try
            {
                if (loaded)
                {
                    Load(entry);
                }
                else
                {
                    Unload(entry);
                }

                Save();
            }
            finally
            {
                _work.Release();
            }
        });
    }

    /// <summary>Must be called under <see cref="_gate"/>.</summary>
    private void Changed() => Interlocked.Increment(ref _version);

    private void Scan()
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (string path in Directory.EnumerateFiles(Folder, "*.dll"))
                {
                    files.Add(Path.GetFileName(path));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder lists nothing; the plug-ins already loaded stay loaded.
            _logger.Warning(exception, "The plug-in folder {Folder} could not be read", Folder);
        }

        lock (_gate)
        {
            bool changed = false;
            foreach (string name in files.Where(name => !_entries.ContainsKey(name)))
            {
                _entries[name] = new Entry(name);
                changed = true;
            }

            // A file that has gone is forgotten, unless the plug-in is loaded or on its way; a loaded one keeps its entry until it is unloaded.
            #pragma warning disable S2971 // The collection is changed by Load and Unload while it is walked, so the copy is needed.
            foreach (Entry entry in _entries.Values.ToList().Where(entry => !files.Contains(entry.FileName) && entry.State is PluginState.Unloaded or PluginState.Failed))
            #pragma warning restore S2971
            {
                _entries.Remove(entry.FileName);
                changed = true;
            }

            if (changed)
            {
                Changed();
            }
        }
    }

    /// <summary>Loads one plug-in file and registers what it holds. Runs under <see cref="_work"/>.</summary>
    private void Load(Entry entry)
    {
        PluginLoadContext context = new($"plugin:{entry.FileName}", Folder);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(Path.Combine(Folder, entry.FileName))));
            List<IFileViewer> viewers = [];
            List<IFileEditor> editors = [];
            foreach (Type type in PluginTypes(assembly))
            {
                if (!typeof(IFileViewer).IsAssignableFrom(type) && !typeof(IFileEditor).IsAssignableFrom(type))
                {
                    continue;
                }

                object instance = ActivatorUtilities.CreateInstance(_services, type);
                if (instance is IFileViewer viewer)
                {
                    viewers.Add(viewer);
                }

                if (instance is IFileEditor editor)
                {
                    editors.Add(editor);
                }
            }

            if (viewers.Count == 0 && editors.Count == 0)
            {
                // Not a fault in the file, only one with nothing for F3 or F4: the dialog says so when there is no error text.
                context.Unload();
                Finish(entry, PluginState.Failed, null);
                _logger.Warning("Plug-in {FileName} holds no viewer or editor", entry.FileName);
                return;
            }

            foreach (IFileViewer viewer in viewers)
            {
                _viewers.Add(viewer);
            }

            foreach (IFileEditor editor in editors)
            {
                _editors.Add(editor);
            }

            lock (_gate)
            {
                entry.Context = context;
                entry.Viewers.AddRange(viewers);
                entry.Editors.AddRange(editors);
                entry.State = PluginState.Loaded;
                entry.Error = null;
                Changed();
            }

            _logger.Information("Loaded plug-in {FileName}: {Viewers} viewer(s), {Editors} editor(s)", entry.FileName, viewers.Count, editors.Count);
        }
        catch (Exception exception)
        {
            context.Unload();
            _logger.Warning(exception, "Plug-in {FileName} could not be loaded", entry.FileName);
            Finish(entry, PluginState.Failed, exception.Message);
        }
    }

    /// <summary>Removes a loaded plug-in's handlers and releases its context. Runs under <see cref="_work"/>.</summary>
    private void Unload(Entry entry)
    {
        List<IFileViewer> viewers;
        List<IFileEditor> editors;
        PluginLoadContext? context;
        lock (_gate)
        {
            viewers = [.. entry.Viewers];
            editors = [.. entry.Editors];
            context = entry.Context;
            entry.Viewers.Clear();
            entry.Editors.Clear();
            entry.Context = null;
        }

        foreach (IFileViewer viewer in viewers)
        {
            _viewers.Remove(viewer);
        }

        foreach (IFileEditor editor in editors)
        {
            _editors.Remove(editor);
        }

        context?.Unload();
        Finish(entry, PluginState.Unloaded, null);
        _logger.Information("Unloaded plug-in {FileName}", entry.FileName);
    }

    private void Finish(Entry entry, PluginState state, string? error)
    {
        lock (_gate)
        {
            entry.State = state;
            entry.Error = error;
            Changed();
        }
    }

    /// <summary>Public, concrete, non-generic classes. A type that cannot be loaded is skipped and the rest still count.</summary>
    private static IEnumerable<Type> PluginTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToArray();
        }

        return types.Where(type => type is { IsClass: true, IsAbstract: false, IsVisible: true, IsGenericTypeDefinition: false });
    }

    private IReadOnlyList<string> ReadSaved()
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(_settings.Get(Settings.LoadedPlugins) ?? "[]") ?? [];
        }
        catch (JsonException exception)
        {
            _logger.Warning(exception, "The saved plug-in list is not valid and is ignored");
            return [];
        }
    }

    private void Save()
    {
        string[] names;
        lock (_gate)
        {
            names = _wanted.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        _settings.Set(Settings.LoadedPlugins, JsonSerializer.Serialize(names));
        _settings.Write();
    }

    private sealed class Entry(string fileName)
    {
        public string FileName { get; } = fileName;

        public PluginState State { get; set; }

        public string? Error { get; set; }

        public PluginLoadContext? Context { get; set; }

        public List<IFileViewer> Viewers { get; } = [];

        public List<IFileEditor> Editors { get; } = [];
    }
}
