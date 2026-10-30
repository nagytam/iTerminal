using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using System.Diagnostics;

namespace iTerminal.Core.Dialogs;

public class ProcessManagerDialog : Dialog
{
    private ListBox<Process> _listBox { get; set; }
    private CloseButton _closeButton { get; set; }
    private TextBox _textbox { get; set; }

    public ProcessManagerDialog(
        IBeepService beepService,
        ProcessService processService, 
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService) 
        : base(nameof(ProcessManagerDialog), themeService)
    {
        processService ??= new ProcessService();

        Title = iConsole.Localization.UiText.Get("Dialog.ProcessManager", "Process manager");
        _textbox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Filter", Placeholder = "process name or id" };
        AddControl(_textbox);
        var listModel = new ListBoxModel<Process>(processService.Processes)
        {
            TransformItem = TransformItem,
            Filter = (s, ct) => s.Value.Contains(_textbox.Text, StringComparison.InvariantCultureIgnoreCase),
        };
        _listBox = new ListBox<Process>(listModel, themeService)
        {
            EmptyText = " No process matches the filter.",
        };
        AddControl(_listBox);

        // Subscribed once, here. It used to be added in OnInitializeLayout, which runs on every
        // resize and every reopening, so each keystroke in the filter rebuilt the list once per
        // layout pass so far.
        _textbox.PropertyChanged += (s, e) =>
        {
            // RefreshItems, not UpdateModel: the processes did not change, the filter did, and
            // UpdateModel only re-projects after a change to the items — so the box filtered nothing.
            _listBox.RefreshItems();
            Refresh();
        };
        _closeButton = new CloseButton(themeService);
        AddControl(_closeButton);
    }

    private ListItem<Process> TransformItem(Process item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Process>(item)
        {
            Value = $"{item.Id,6} {item.ProcessName}".Shrink((int)_listBox.ViewSize.Width - 2) ?? string.Empty,
            IsEnabled = true
        };
        return transformedItem;
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _textbox.Position = new Position(1, 1);
        _textbox.ViewSize = new Size(ViewSize.Width - 2, 1);
        _textbox.ScrollableModel.ViewSize = _textbox.ViewSize;
        _listBox.Position = new Position(1, 2);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 5);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;
        _listBox.ScrollableModel.ScrollableSize = new Size(0, 1);
        _closeButton.Position = new Position(1, ViewSize.Height - 2);
        _closeButton.ViewSize = new Size(ViewSize.Width - 2, 1);

        base.OnInitializeLayout();
    }

    internal ListBox<Process> ListBoxForTest => _listBox;
    internal TextBox FilterBoxForTest => _textbox;

    /// <summary>
    /// Up/Down/PageUp/PageDown move the list while the filter has focus, so narrowing and choosing
    /// is one motion — the arrows otherwise went to the filter box, which has nothing to do with them.
    /// </summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Type", UiText.Get("DialogHint.FilterProcesses", "Filter the processes"));
        yield return ("Up/Down", UiText.Get("DialogHint.MoveOverProcesses", "Move over the processes, even from the filter box"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Ctrl+Home/End", UiText.Get("DialogHint.JumpFirstLastProcess", "Jump to the first or last process"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_textbox.IsFocused
            && keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            return await _listBox.HandleKeyEventAsync(keyEvent);
        }

        // Home and End belong to the filter's cursor, so Ctrl+Home and Ctrl+End reach the first and the last process.
        if (_textbox.IsFocused && keyEvent.Control && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.Home or ConsoleKey.End)
        {
            return await _listBox.HandleKeyEventAsync(new KeyEvent(keyEvent.KeyChar, keyEvent.ConsoleKey, false, false, false));
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }
}
