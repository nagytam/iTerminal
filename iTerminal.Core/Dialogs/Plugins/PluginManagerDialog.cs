using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.Plugins;

namespace iTerminal.Core.Dialogs.Plugins;

/// <summary>
/// Tools > Plugin manager: the viewer (F3) and editor (F4) plug-ins in the plug-in folder, one row each. A checked row is
/// loaded: Space or Enter checks or unchecks it, and the plug-in loads or unloads in the background, with "loading" or
/// "unloading" shown until it is done. R reads the folder again, so a plug-in copied in while the app runs appears.
/// The rows are redrawn from the manager's state on the idle pass, so the dialog never waits for a load.
/// </summary>
public class PluginManagerDialog : Dialog
{
    private const int ListRows = 10;
    private const int DetailRows = 2;
    private const int DialogWidth = 76;

    private readonly IPluginManagerService _plugins;
    private readonly CheckList _list;
    private readonly InfoLines _details;
    private IReadOnlyList<PluginInfo> _shown = [];
    private long _shownVersion = -1;

    public PluginManagerDialog(IPluginManagerService plugins, IThemeService themeService)
        : base(nameof(PluginManagerDialog), themeService)
    {
        _plugins = plugins;
        Title = UiText.Get("Plugins.Title", "Plugin manager");
        _list = new CheckList(themeService);
        _list.CheckedChanged += OnCheckedChanged;
        _list.Activated += (_, index) =>
        {
            if (index >= 0 && index < _list.Items.Count)
            {
                _list.SetChecked(index, !_list.Items[index].Checked);
            }
        };
        _details = new InfoLines(themeService);
        AddControl(_list);
        AddControl(_details);
    }

    /// <summary>The plug-ins as the dialog shows them, in row order; a test reads it.</summary>
    public IReadOnlyList<PluginInfo> Shown => _shown;

    /// <summary>Called each time the dialog opens: reads the folder again and shows the plug-ins as they are now.</summary>
    public void Load()
    {
        _ = _plugins.RescanAsync();
        RebuildRows();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        RebuildRows();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, ListRows + DetailRows + 3));
        }

        _list.Position = new Position(2, 1);
        _list.ViewSize = new Size(DialogWidth - 4, ListRows);
        _details.Position = new Position(2, ListRows + 2);
        _details.ViewSize = new Size(DialogWidth - 4, DetailRows);
        base.OnInitializeLayout();
        UpdateDetails();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_plugins.Version != _shownVersion)
        {
            RebuildRows();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("R", UiText.Get("Plugins.Hint.Rescan", "Rescan the plug-in folder"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled)
        {
            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.R)
        {
            _ = _plugins.RescanAsync();
            return true;
        }

        return false;
    }

    /// <summary>
    /// A check that differs from its plug-in's state starts the load or unload, and the rows are redrawn at once, so the
    /// row shows "loading" or "unloading" (or goes back to its checked state when the manager refuses the change).
    /// </summary>
    private void OnCheckedChanged(object? sender, EventArgs e)
    {
        for (int row = 0; row < _shown.Count && row < _list.Items.Count; row++)
        {
            PluginInfo plugin = _shown[row];
            bool wanted = _list.Items[row].Checked;
            if (wanted && plugin.State is PluginState.Unloaded or PluginState.Failed)
            {
                _ = _plugins.SetLoadedAsync(plugin.FileName, true);
            }
            else if (!wanted && plugin.State == PluginState.Loaded)
            {
                _ = _plugins.SetLoadedAsync(plugin.FileName, false);
            }
        }

        // Safe here: CheckList raises this only after it has finished changing its items.
        RebuildRows();
    }

    private void RebuildRows()
    {
        // Read the version first: a change made while the list is built makes the next idle pass build it again.
        _shownVersion = _plugins.Version;
        _shown = _plugins.Plugins;

        List<string> texts = [];
        List<string> checkedTexts = [];
        foreach (PluginInfo plugin in _shown)
        {
            string text = $"{plugin.FileName,-30} {StatusText(plugin)}";
            texts.Add(text);
            if (plugin.State is PluginState.Loaded or PluginState.Loading)
            {
                checkedTexts.Add(text);
            }
        }

        _list.EmptyText = string.Format(
            UiText.Get("Plugins.Empty", "No plug-ins in {0}. Copy a plug-in .dll there, then press R."),
            _plugins.Folder);
        _list.SetItems(texts, checkedTexts);
        UpdateDetails();
        StateHasChanged();
    }

    private static string StatusText(PluginInfo plugin) => plugin.State switch
    {
        PluginState.Loaded => string.Format(UiText.Get("Plugins.Status.Loaded", "loaded, {0} viewer(s), {1} editor(s)"), plugin.Viewers, plugin.Editors),
        PluginState.Loading => UiText.Get("Plugins.Status.Loading", "loading…"),
        PluginState.Unloading => UiText.Get("Plugins.Status.Unloading", "unloading…"),
        PluginState.Failed => UiText.Get("Plugins.Status.Failed", "failed"),
        _ => UiText.Get("Plugins.Status.Unloaded", "not loaded"),
    };

    /// <summary>The folder, and why the plug-in under the cursor failed, if it did.</summary>
    private void UpdateDetails()
    {
        List<InfoLines.Line> lines =
        [
            new(string.Format(UiText.Get("Plugins.Folder", "Folder: {0}"), _plugins.Folder)),
        ];

        if (_list.CursorIndex < _shown.Count && _shown[_list.CursorIndex] is { State: PluginState.Failed } failed)
        {
            // A failure with no error text means the file held no viewer or editor.
            string reason = failed.Error ?? string.Format(UiText.Get("Plugins.Error.NoHandlers", "{0} holds no viewer or editor plug-in."), failed.FileName);
            lines.Add(new(reason, Color.Red));
        }

        _details.Lines = lines;
    }
}
