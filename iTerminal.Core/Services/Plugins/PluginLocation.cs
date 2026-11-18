namespace iTerminal.Core.Services.Plugins;

/// <summary>
/// The folder the viewer and editor plug-ins are read from: one assembly (<c>.dll</c>) per plug-in, beside the database.
/// Registered once in the container, so a test that moves <c>DataDirectory</c> moves the plug-in folder with it.
/// </summary>
public sealed record PluginLocation(string Directory);
