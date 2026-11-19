using System.Reflection;
using System.Runtime.Loader;

namespace iTerminal.Core.Services.Plugins;

/// <summary>
/// The collectible context one plug-in assembly is loaded into, so unloading it lets its code be collected.
/// <para>
/// An assembly the application already has loaded (iConsole, iTerminal.Core, the framework) is not loaded again: this
/// context answers null for it and the default context serves it, so a plug-in's <c>IFileViewer</c> is the application's own.
/// Any other assembly the plug-in needs is read from the plug-in folder as bytes, so the file is not locked and can be
/// replaced while the application runs.
/// </para>
/// </summary>
internal sealed class PluginLoadContext(string name, string folder) : AssemblyLoadContext(name, isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || IsLoadedByHost(assemblyName.Name))
        {
            return null;
        }

        string path = Path.Combine(folder, assemblyName.Name + ".dll");
        return File.Exists(path)
            ? LoadFromStream(new MemoryStream(File.ReadAllBytes(path)))
            : null;
    }

    private static bool IsLoadedByHost(string simpleName) =>
        AssemblyLoadContext.Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
}
