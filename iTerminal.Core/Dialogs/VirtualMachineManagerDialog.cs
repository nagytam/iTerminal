using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.VirtualMachines;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The virtual machines of the local libvirt hypervisor, and what can be done to each: S (or Enter on a
/// shut-off machine) starts it, D shuts it down, Shift+X powers it off, R restarts it, P pauses or resumes,
/// V saves its state, C opens its console and F5 lists them again. Anything that stops or restarts a guest
/// asks y/n first.
///
/// <list type="bullet">
/// <item>Every <c>virsh</c> call runs on a background task. A worker only stores its answer under a lock;
/// <see cref="OnIdle"/> applies it on the UI thread, and a listing that was overtaken by a newer one is dropped.</item>
/// <item>The list is read again every few seconds while the dialog is open, because a guest can change state
/// outside this program.</item>
/// <item>The dialog has no text box, so the letter keys are commands, never typed text.</item>
/// </list>
/// </summary>
public class VirtualMachineManagerDialog : Dialog
{
    /// <summary>How often the list is read again while the dialog is open.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const int DetailRows = 3;

    private readonly IVirtualMachineService _service;
    private readonly ConcurrentObservableCollection<VirtualMachine> _machines = [];
    private readonly ListBox<VirtualMachine> _listBox;
    private readonly ScrollBar _scrollBar;
    private readonly InfoLines _details;
    private readonly CloseButton _closeButton;

    // Written by background tasks under _gate, read by OnIdle.
    private readonly object _gate = new();
    private long _latestRequest;
    private (long Request, IReadOnlyList<VirtualMachine>? Machines, string? Error)? _listed;
    private (string Message, bool IsError)? _finished;
    private volatile bool _listingInFlight;

    // Touched on the UI thread only.
    private bool _loaded;
    private string _listError = string.Empty;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _isBusy;
    private DateTime _nextPoll = DateTime.UtcNow;
    private (VirtualMachine Machine, VirtualMachineAction Action)? _awaiting;
    private string _shownDetails = string.Empty;

    public VirtualMachineManagerDialog(IVirtualMachineService virtualMachineService, IThemeService themeService)
        : base(nameof(VirtualMachineManagerDialog), themeService)
    {
        _service = virtualMachineService;
        Title = UiText.Get("Dialog.VirtualMachineManager", "Virtual machine manager");

        var listModel = new ListBoxModel<VirtualMachine>(_machines)
        {
            TransformItem = TransformItem,
        };
        _listBox = new ListBox<VirtualMachine>(listModel, themeService)
        {
            EmptyText = ReadingText,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel,
        };
        _details = new InfoLines(themeService);
        _closeButton = new CloseButton(themeService);

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_details);
        AddControl(_closeButton);
    }

    private static string ReadingText => UiText.Get("VirtualMachineManager.Reading", " Reading the virtual machines…");

    /// <summary>The machine under the cursor, or null when the list is empty.</summary>
    public VirtualMachine? SelectedMachine
    {
        get
        {
            var listItems = _listBox.ListModel.ListItems;
            var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < listItems.Count ? listItems[index].Item : null;
        }
    }

    /// <summary>The line of text the dialog shows under the list, for a test to read.</summary>
    public string DetailsText => _details.Text;

    /// <summary>The last status line.</summary>
    public string Status => _status;

    /// <summary>The action started last; it exists so a test can wait for it.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>The machine and action waiting for a y or n, if any.</summary>
    public (VirtualMachine Machine, VirtualMachineAction Action)? AwaitingConfirmation => _awaiting;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        RequestListing();
    }

    public override void OnIdle()
    {
        ApplyFinishedAction();
        ApplyListing();

        if (!_isBusy && !_listingInFlight && DateTime.UtcNow >= _nextPoll)
        {
            RequestListing();
        }

        _listBox.EmptyText = _listError.Length > 0
            ? " " + _listError
            : _loaded ? UiText.Get("VirtualMachineManager.None", " No virtual machines found.") : ReadingText;
        UpdateDetails();
        base.OnIdle();
    }

    private ListItem<VirtualMachine> TransformItem(VirtualMachine item, CancellationToken cancellationToken)
    {
        return new ListItem<VirtualMachine>(item)
        {
            Value = $"{item.Name,-32} {item.StateText}",
            IsEnabled = true,
        };
    }

    // ---- listing --------------------------------------------------------------------------------

    /// <summary>Starts a background read of the list. A read already in flight is left to finish.</summary>
    private void RequestListing()
    {
        _nextPoll = DateTime.UtcNow + PollInterval;
        if (_listingInFlight)
        {
            return;
        }

        long request;
        lock (_gate)
        {
            request = ++_latestRequest;
        }

        _listingInFlight = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var machines = await _service.ListAsync();
                lock (_gate)
                {
                    _listed = (request, machines, null);
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _listed = (request, null, exception.Message);
                }
            }
            finally
            {
                _listingInFlight = false;
            }
        });
    }

    private void ApplyListing()
    {
        (long Request, IReadOnlyList<VirtualMachine>? Machines, string? Error)? listed;
        lock (_gate)
        {
            listed = _listed;
            _listed = null;
        }

        if (listed is not { } answer || answer.Request != _latestRequest)
        {
            return;
        }

        _loaded = true;
        _listError = answer.Error ?? string.Empty;
        if (answer.Machines is null)
        {
            return;
        }

        var selected = SelectedMachine?.Name;
        _machines.ReplaceAll(answer.Machines.ToList());
        _listBox.UpdateModel();
        RestoreCursor(selected);
    }

    /// <summary>Puts the cursor back on the machine it was on, so a refresh does not move it.</summary>
    private void RestoreCursor(string? name)
    {
        if (name is null)
        {
            return;
        }

        var index = _listBox.ListModel.ListItems.ToList().FindIndex(item => item.Item.Name == name);
        if (index >= 0)
        {
            _listBox.ScrollableModel.CursorPosition = new Position(0, index);
        }
    }

    // ---- actions --------------------------------------------------------------------------------

    /// <summary>Runs an action on the machine, or asks for the y/n first when it stops or restarts a guest.</summary>
    private void Request(VirtualMachine machine, VirtualMachineAction action)
    {
        if (!VirtualMachineActions.IsAvailable(machine, action))
        {
            SetStatus(string.Format(UiText.Get("VirtualMachineManager.NotAvailable", "That is not possible for {0} in its current state."), machine.Name), isError: true);
            return;
        }

        if (_isBusy)
        {
            SetStatus(UiText.Get("VirtualMachineManager.Busy", "Still working on the last request."), isError: true);
            return;
        }

        if (VirtualMachineActions.NeedsConfirmation(action))
        {
            _awaiting = (machine, action);
            return;
        }

        Execute(machine, action);
    }

    private void Execute(VirtualMachine machine, VirtualMachineAction action)
    {
        _isBusy = true;
        SetStatus(string.Format(UiText.Get("VirtualMachineManager.Working", "Working on {0}…"), machine.Name), isError: false);
        var name = machine.Name;
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                var result = await _service.RunAsync(name, action);
                lock (_gate)
                {
                    _finished = (result.Message, !result.Success);
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _finished = (exception.Message, true);
                }
            }
        });
    }

    private void ApplyFinishedAction()
    {
        (string Message, bool IsError)? finished;
        lock (_gate)
        {
            finished = _finished;
            _finished = null;
        }

        if (finished is not { } outcome)
        {
            return;
        }

        _isBusy = false;
        SetStatus(outcome.Message, outcome.IsError);
        RequestListing();
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    private void UpdateDetails()
    {
        string line1;
        if (_awaiting is { } asked)
        {
            line1 = Question(asked.Machine, asked.Action);
        }
        else if (SelectedMachine is { } machine)
        {
            line1 = $" {machine.Name}: {machine.StateText}";
        }
        else
        {
            line1 = string.Empty;
        }

        var lines = new List<InfoLines.Line>
        {
            new(line1),
            new(_status.Length > 0 ? " " + _status : string.Empty, _status.Length > 0 && _statusIsError ? Color.Red : Color.Green),
            new(_isBusy ? " Working..." : string.Empty),
        };

        var text = string.Join('\n', lines.Select(line => line.Text));
        if (text != _shownDetails)
        {
            _shownDetails = text;
            _details.Lines = lines;
        }
    }

    private static string Question(VirtualMachine machine, VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Shutdown => string.Format(UiText.Get("VirtualMachineManager.Confirm.Shutdown", " Shut down {0}? (y/n)"), machine.Name),
        VirtualMachineAction.PowerOff => string.Format(UiText.Get("VirtualMachineManager.Confirm.PowerOff", " Power off {0} now, losing unsaved work? (y/n)"), machine.Name),
        VirtualMachineAction.Restart => string.Format(UiText.Get("VirtualMachineManager.Confirm.Restart", " Restart {0}? (y/n)"), machine.Name),
        _ => string.Format(UiText.Get("VirtualMachineManager.Confirm.SaveState", " Save the state of {0} and stop it? (y/n)"), machine.Name),
    };

    // ---- layout and keys ------------------------------------------------------------------------

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        var listHeight = Math.Max(1, ViewSize.Height - 6);
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, listHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, listHeight);

        _details.Position = new Position(2, 1 + listHeight);
        _details.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), DetailRows);

        _closeButton.Position = new Position(1, ViewSize.Height - 2);
        _closeButton.ViewSize = new Size(ViewSize.Width - 2, 1);
        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter / S", UiText.Get("DialogHint.StartVirtualMachine", "Start"));
        yield return ("D", UiText.Get("DialogHint.ShutDownVirtualMachine", "Shut down (y/n)"));
        yield return ("Shift+X", UiText.Get("DialogHint.PowerOffVirtualMachine", "Power off (y/n)"));
        yield return ("R", UiText.Get("DialogHint.RestartVirtualMachine", "Restart (y/n)"));
        yield return ("P", UiText.Get("DialogHint.PauseResumeVirtualMachine", "Pause or resume"));
        yield return ("V", UiText.Get("DialogHint.SaveVirtualMachine", "Save state (y/n)"));
        yield return ("C", UiText.Get("DialogHint.ConsoleVirtualMachine", "Open the console"));
        yield return ("F5", UiText.Get("DialogHint.RefreshVirtualMachines", "Refresh"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_awaiting is not null && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            var (machine, action) = _awaiting.Value;
            _awaiting = null;
            if (!keyEvent.Alt && !keyEvent.Control && char.ToUpperInvariant(keyEvent.KeyChar) == 'Y')
            {
                Execute(machine, action);
            }
            else
            {
                SetStatus(UiText.Get("VirtualMachineManager.Cancelled", "Cancelled."), isError: false);
            }

            return true;
        }

        // Esc is claimed by the Dialog before any of this runs, so it still closes the dialog.
        if (keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            SetStatus(UiText.Get("VirtualMachineManager.Refreshing", "Refreshing…"), isError: false);
            RequestListing();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            // Enter starts the machine under the cursor when it is shut off, and does nothing otherwise.
            if (SelectedMachine is { } entered && VirtualMachineActions.IsAvailable(entered, VirtualMachineAction.Start))
            {
                Request(entered, VirtualMachineAction.Start);
            }

            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && keyEvent.KeyPressType == KeyPressType.Down)
        {
            var letter = char.ToUpperInvariant(keyEvent.KeyChar);
            var action = letter switch
            {
                'S' => (VirtualMachineAction?)VirtualMachineAction.Start,
                'D' => VirtualMachineAction.Shutdown,
                'R' => VirtualMachineAction.Restart,
                'P' => null,
                'V' => VirtualMachineAction.SaveState,
                'C' => VirtualMachineAction.Console,
                'X' when keyEvent.Shift => VirtualMachineAction.PowerOff,
                _ => (VirtualMachineAction?)null,
            };
            if (letter == 'P')
            {
                action = SelectedMachine?.State == VirtualMachineState.Paused ? VirtualMachineAction.Resume : VirtualMachineAction.Pause;
            }

            if (action is { } chosen)
            {
                if (SelectedMachine is { } machine)
                {
                    Request(machine, chosen);
                }
                else
                {
                    SetStatus(UiText.Get("VirtualMachineManager.NoneSelected", "No virtual machine is selected."), isError: true);
                }

                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }
}
