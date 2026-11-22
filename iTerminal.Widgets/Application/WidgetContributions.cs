using System.Reflection;
using iConsole.Commands;
using iTerminal.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Widgets.Application;

/// <summary>A Tools menu entry that a widget adds.</summary>
public sealed record WidgetToolsMenuEntry(string Group, ICommand Command) : IToolsMenuContribution;

/// <summary>A hot-key button that a widget adds to the status bar's row.</summary>
public sealed record WidgetHotKey(string Label, int Width, ICommand Command) : IStatusBarHotKey;

/// <summary>The widgets' assembly, scanned for key-map command names.</summary>
public sealed record WidgetsCommandAssembly(Assembly Assembly) : ICommandAssemblyContribution;

/// <summary>A widget dialog the AI assistant can open by any of its names.</summary>
public sealed class OpenableWidgetDialog<TDialog>(params string[] names) : IOpenableDialogContribution
    where TDialog : iConsole.Dialogs.Dialog
{
    public IReadOnlyList<string> Names { get; } = names;

    public iConsole.Dialogs.Dialog Resolve(IServiceProvider services) => services.GetRequiredService<TDialog>();
}
