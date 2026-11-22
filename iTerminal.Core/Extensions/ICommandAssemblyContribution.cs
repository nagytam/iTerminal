using System.Reflection;

namespace iTerminal.Core.Extensions;

/// <summary>
/// An assembly outside iTerminal.Core whose commands the key map may bind by name (keymap.json). Core scans its own
/// assemblies; a feature project registers its assembly here.
/// </summary>
public interface ICommandAssemblyContribution
{
    Assembly Assembly { get; }
}
