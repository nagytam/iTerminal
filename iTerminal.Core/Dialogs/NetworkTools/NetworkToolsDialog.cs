using System.Collections.Concurrent;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Services.NetworkTools;

namespace iTerminal.Core.Dialogs.NetworkTools;

/// <summary>
/// Network tools: ping, traceroute, DNS and reverse DNS lookups, whois, a TCP port check and the headers of a web address,
/// all for a target typed in the box. Tab moves between the tool, the target and the output; Enter runs the tool (while the
/// tool list is closed); Esc stops a run that is going, and closes the dialog otherwise.
///
/// <para>Nothing here waits on the network. A run is a background task that hands each line to a queue; <see cref="OnIdle"/>
/// moves the queued lines into the output on the UI thread, and the finished outcome is applied the same way. Closing cancels
/// the run in flight.</para>
/// </summary>
public class NetworkToolsDialog : Dialog
{
    internal const int MaxOutputLines = 2000;

    private static readonly NetworkToolKind[] Kinds = Enum.GetValues<NetworkToolKind>();

    internal sealed record ToolChoice(NetworkToolKind Kind, string Name, string Description);

    private readonly INetworkToolService _service;
    private readonly ComboBox<ToolChoice> _tool;
    private readonly TextBox _target;
    private readonly Label _description;
    private readonly ConcurrentObservableCollection<string> _output = [];
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly ListBox<string> _outputBox;
    private readonly ScrollBar _scrollBar;
    private readonly Label _status;

    private CancellationTokenSource? _cancellation;
    private volatile bool _running;
    private volatile string? _finishedStatus;
    private int _written;
    private Task _run = Task.CompletedTask;

    public NetworkToolsDialog(INetworkToolService service, IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : base(nameof(NetworkToolsDialog), themeService)
    {
        _service = service;
        Title = UiText.Get("Dialog.NetworkTools", "Network tools");

        _tool = new ComboBox<ToolChoice>(themeService)
        {
            Label = UiText.Get("NetworkTools.Field.Tool", "Tool"),
            DisplaySelector = choice => choice.Name,
            MaxDropDownRows = Kinds.Length,
        };
        _tool.SetItems(Kinds.Select(kind => new ToolChoice(kind, NameOf(kind), DescriptionOf(kind))));
        _tool.SelectedIndex = 0;

        _target = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("NetworkTools.Field.Target", "Target"),
        };
        _description = new Label(string.Empty, themeService);
        _outputBox = new ListBox<string>(new ListBoxModel<string>(_output), themeService)
        {
            EmptyText = UiText.Get("NetworkTools.Empty", "Choose a tool, type a target and press Enter."),
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollBarOrientation = Orientation.Vertical,
            ScrollableModel = _outputBox.ScrollableModel,
        };
        _status = new Label(string.Empty, themeService);

        AddControl(_tool);
        AddControl(_target);
        AddControl(_description);
        AddControl(_outputBox);
        AddControl(_scrollBar);
        AddControl(_status);
        ShowDescription();
    }

    #region Test seams

    internal ComboBox<ToolChoice> ToolBox => _tool;
    internal TextBox TargetBox => _target;
    internal string Status => _status.Text;
    internal bool IsRunning => _running;

    /// <summary>The output shown so far.</summary>
    internal IReadOnlyList<string> Output => [.. _output];

    /// <summary>The run the last Enter started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task RunTask => _run;

    /// <summary>Applies the queued lines and outcome now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        int inner = (int)ViewSize.Width - 2;
        int height = (int)ViewSize.Height - 2;
        if (inner > 0 && height > 0)
        {
            int width = Math.Max(6, inner - 2);
            _tool.Position = new Position(1, 1);
            _tool.ViewSize = new Size(width, 1);
            _target.Position = new Position(1, 2);
            _target.ViewSize = new Size(width, 1);
            _target.ScrollableModel.ViewSize = _target.ViewSize;
            _description.Position = new Position(1, 3);
            _description.ViewSize = new Size(Math.Max(1, inner - 2), 1);

            // The output takes every row between the description and the status line, with the scroll bar on its right edge.
            int outputHeight = Math.Max(1, height - 5);
            _outputBox.Position = new Position(1, 4);
            _outputBox.ViewSize = new Size(Math.Max(1, inner - 1), outputHeight);
            _outputBox.ScrollableModel.ViewSize = _outputBox.ViewSize;
            _scrollBar.Position = new Position(inner, 4);
            _scrollBar.ViewSize = new Size(1, outputHeight);
            _status.Position = new Position(1, height);
            _status.ViewSize = new Size(Math.Max(1, inner - 2), 1);
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_running)
        {
            yield return ("Esc", UiText.Get("NetworkTools.Hint.Stop", "Stop the run"));
            yield break;
        }

        yield return ("Enter", UiText.Get("NetworkTools.Hint.Run", "Run the tool"));
        yield return ("Esc", UiText.Get("Common.Close", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (plain && keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey == ConsoleKey.Enter && !_tool.IsOpen)
        {
            Run();
            StateHasChanged();
            return true;
        }

        // Esc stops a run first; the next Esc closes the dialog.
        if (plain && keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey == ConsoleKey.Escape && _running)
        {
            CancelRun();
            StateHasChanged();
            return true;
        }

        bool handled = await base.HandleKeyEventAsync(keyEvent);
        StateHasChanged();
        return handled;
    }

    public override void OnIdle()
    {
        // The outcome is read before the queue is drained: every line is queued before the outcome is set, so none is missed.
        string? finished = _finishedStatus;
        DrainOutput();
        if (finished is not null)
        {
            _finishedStatus = null;
            _status.Text = finished;
            ShowDescription();
        }

        base.OnIdle();
    }

    public override void Close()
    {
        CancelRun();
        base.Close();
    }

    /// <summary>Starts the selected tool on the target. A run still going is left alone.</summary>
    internal void Run()
    {
        if (_running)
        {
            return;
        }

        string target = _target.Text.Trim();
        if (target.Length == 0 || _tool.SelectedItem is not { } choice)
        {
            _status.Text = UiText.Get("NetworkTools.Status.NeedTarget", "Type a host name or an address first.");
            return;
        }

        _output.Clear();
        _written = 0;
        while (_pending.TryDequeue(out _))
        {
        }

        _finishedStatus = null;
        _running = true;
        _status.Text = UiText.Get("NetworkTools.Status.Running", "Running…");
        ShowDescription();

        CancellationTokenSource cancellation = _cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        NetworkToolKind kind = choice.Kind;
        _run = Task.Run(async () =>
        {
            string outcome;
            try
            {
                await _service.RunAsync(kind, target, line => _pending.Enqueue(line), token);
                outcome = UiText.Get("NetworkTools.Status.Done", "Finished.");
            }
            catch (OperationCanceledException)
            {
                outcome = UiText.Get("NetworkTools.Status.Stopped", "Stopped.");
            }
            catch (Exception exception)
            {
                // A bad target or an unexpected failure says so on the status line rather than leaving "Running…" up.
                outcome = exception.Message;
            }
            finally
            {
                _running = false;
            }

            _finishedStatus = outcome;
        });
    }

    private void CancelRun()
    {
        _cancellation?.Cancel();
    }

    private void DrainOutput()
    {
        while (_pending.TryDequeue(out string? line))
        {
            if (_written == MaxOutputLines)
            {
                _output.Add(UiText.Get("NetworkTools.Output.Cut", "… the output is longer; the first lines are kept."));
                _written++;
            }
            else if (_written < MaxOutputLines)
            {
                _output.Add(line);
                _written++;
            }
        }
    }

    private void ShowDescription()
    {
        _description.Text = _tool.SelectedItem?.Description ?? string.Empty;
    }

    private static string NameOf(NetworkToolKind kind) => kind switch
    {
        NetworkToolKind.Ping => UiText.Get("NetworkTools.Tool.Ping", "Ping"),
        NetworkToolKind.Traceroute => UiText.Get("NetworkTools.Tool.Traceroute", "Traceroute"),
        NetworkToolKind.DnsLookup => UiText.Get("NetworkTools.Tool.DnsLookup", "DNS lookup"),
        NetworkToolKind.ReverseDns => UiText.Get("NetworkTools.Tool.ReverseDns", "Reverse DNS"),
        NetworkToolKind.Whois => UiText.Get("NetworkTools.Tool.Whois", "Whois"),
        NetworkToolKind.PortCheck => UiText.Get("NetworkTools.Tool.PortCheck", "Port check"),
        NetworkToolKind.HttpHeaders => UiText.Get("NetworkTools.Tool.HttpHeaders", "HTTP headers"),
        NetworkToolKind.Scan => UiText.Get("NetworkTools.Tool.Scan", "Subnet scan"),
        _ => kind.ToString(),
    };

    private static string DescriptionOf(NetworkToolKind kind) => kind switch
    {
        NetworkToolKind.Ping => UiText.Get("NetworkTools.Tool.Ping.Description", "Send echo requests to a host and time the replies."),
        NetworkToolKind.Traceroute => UiText.Get("NetworkTools.Tool.Traceroute.Description", "Show the routers between this computer and a host."),
        NetworkToolKind.DnsLookup => UiText.Get("NetworkTools.Tool.DnsLookup.Description", "Resolve a host name to its addresses."),
        NetworkToolKind.ReverseDns => UiText.Get("NetworkTools.Tool.ReverseDns.Description", "Find the host name registered for an IP address."),
        NetworkToolKind.Whois => UiText.Get("NetworkTools.Tool.Whois.Description", "Ask the registry who holds a domain or an address."),
        NetworkToolKind.PortCheck => UiText.Get("NetworkTools.Tool.PortCheck.Description", "See whether a TCP port on a host accepts connections. Type host:port."),
        NetworkToolKind.HttpHeaders => UiText.Get("NetworkTools.Tool.HttpHeaders.Description", "Request a web address and show the response headers."),
        NetworkToolKind.Scan => UiText.Get("NetworkTools.Tool.Scan.Description", "Ping every address in a subnet such as 192.168.1.0/24 and list the hosts that answer."),
        _ => string.Empty,
    };
}
