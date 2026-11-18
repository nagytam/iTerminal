namespace iTerminal.Core.Services.Plugins;

/// <summary>Where a plug-in is in its life: on disk only, being loaded, loaded, being unloaded, or refused.</summary>
public enum PluginState
{
    Unloaded,
    Loading,
    Loaded,
    Unloading,
    Failed,
}

/// <summary>
/// One plug-in file in the plug-in folder. <see cref="Viewers"/> and <see cref="Editors"/> count what it registered
/// while loaded. <see cref="Error"/> says why it failed; a failed file with no error had no viewer or editor in it.
/// </summary>
public sealed record PluginInfo(string FileName, PluginState State, int Viewers, int Editors, string? Error);

/// <summary>
/// Loads and unloads the viewer (F3) and editor (F4) plug-ins in the plug-in folder, and remembers which ones the user
/// loaded so they load again at the next start-up.
/// </summary>
public interface IPluginManagerService
{
    /// <summary>The folder the plug-in files are read from.</summary>
    string Folder { get; }

    /// <summary>Changes whenever a plug-in's state or the folder's contents change, so a dialog knows when to read again.</summary>
    long Version { get; }

    /// <summary>The plug-in files in the folder, sorted by name, with their state.</summary>
    IReadOnlyList<PluginInfo> Plugins { get; }

    /// <summary>Loads the plug-ins the user loaded last time. Returns how many of them failed to load.</summary>
    Task<int> StartAsync();

    /// <summary>Reads the folder again: new files appear, and files that were removed while unloaded disappear.</summary>
    Task RescanAsync();

    /// <summary>
    /// Loads or unloads one plug-in file. The state changes at once (<see cref="PluginState.Loading"/> or
    /// <see cref="PluginState.Unloading"/>) and the work runs on a background task; the returned task completes when it is done.
    /// Does nothing when the plug-in is already in that state.
    /// </summary>
    Task SetLoadedAsync(string fileName, bool loaded);
}
