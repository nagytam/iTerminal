using iTerminal.Core.Dialogs.Plugins;

namespace iTerminal.Core.Commands;

/// <summary>Tools > Plugin manager. A menu item only: no hot key is taken for it.</summary>
public class PluginManagerDialogCommand(PluginManagerDialog pluginManagerDialog) : ShowDialogCommand<PluginManagerDialog>(pluginManagerDialog)
{
    public override string Title => "Plugin manager…";
    public override string Description => "Load or unload the viewer and editor plug-ins in the plug-in folder.";

    protected override void OnBeforeShow(PluginManagerDialog dialog) => dialog.Load();
}
