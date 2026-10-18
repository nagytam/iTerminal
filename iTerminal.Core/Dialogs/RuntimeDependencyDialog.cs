using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Progress;
using iConsole.Controls.Wizard;
using iConsole.Data;
using iConsole.Display;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The components iTerminal downloads rather than ships, and the way to install or update them. It opens
/// at start-up when something is missing or newer than what is installed, and from Tools at any time.
/// Each dependency takes three rows — its name, its state, and a note or problem — so the dialog keeps
/// the same size while an install runs and the list changes under it.
///
/// <para>The dialog opens as a wizard: an overview of every component first (so the first page is never
/// empty), then one step per dependency, up to date or not (what it is, what is installed, and for the
/// ones that need work a tick for whether to include it) and, when anything needs work, a last step
/// that sums up what Finish will install. The wizard's header and step indicator say where you are. Enter moves on and installs on the last step, Backspace or Left
/// goes back, Space ticks, S skips the ticked versions. Once an install has started
/// the dialog shows the plain list with the install log.</para>
/// </summary>
public class RuntimeDependencyDialog : Dialog
{
    private const int PreferredWidth = 78;
    private const int LogRows = 3;
    private const int RowsPerDependency = 3;
    private const int FixedRows = 10; // Borders, intro, blanks, the log and the hint.

    private readonly IRuntimeDependencyService _service;
    private readonly HashSet<string> _unticked = [];
    private Wizard? _wizard;
    private bool _wizardActive;

    public RuntimeDependencyDialog(IRuntimeDependencyService service, IThemeService themeService)
        : base("Runtime dependencies", themeService)
    {
        _service = service;
        Title = "Runtime dependencies";
    }

    /// <summary>Whether anything is left to install or update.</summary>
    internal bool HasWork => _service.Statuses.Any(status => status.State != RuntimeDependencyState.Installed);

    /// <summary>The dependencies that have something to install or update, in list order.</summary>
    private List<RuntimeDependencyStatus> Actionable() =>
        _service.Statuses.Where(status => status.State != RuntimeDependencyState.Installed).ToList();

    /// <summary>Whether a dependency is ticked for installing. Everything that needs work starts ticked.</summary>
    internal bool IsTicked(string id) => !_unticked.Contains(id);

    /// <summary>The ids Enter would install: the ones that need work and are still ticked.</summary>
    private List<string> TickedIds() => Actionable().Where(status => IsTicked(status.Id)).Select(status => status.Id).ToList();

    /// <summary>Whether the start-up should open this dialog: something is missing, or newer, and not skipped.</summary>
    public bool ShouldOpenAtStartup => _service.ShouldOfferAtStartup;

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int height = (int)Math.Max(FixedRows, Math.Min(FixedRows + RowsPerDependency * _service.Statuses.Count, (long)Parent.ViewSize.Height));
            CenterInParent(new Size(width, height));
        }

        PlaceWizard();
        base.OnInitializeLayout();
    }

    private void PlaceWizard()
    {
        if (_wizard is not null)
        {
            _wizard.Position = new Position(1, 1);
            _wizard.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 3));
            _wizard.OnInitializeLayout();
        }
    }

    /// <summary>Whether the wizard is what the dialog shows now (rather than the list with the install log).</summary>
    internal bool IsWizardActive => _wizardActive && _wizard is not null;

    /// <summary>The wizard, for a test to inspect.</summary>
    internal Wizard? WizardForTest => _wizard;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        BuildWizard();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // The base makes every child visible; the wizard shows only while it is the active view.
        if (_wizard is not null)
        {
            _wizard.IsVisible = _wizardActive;
        }
    }

    private void BuildWizard()
    {
        if (_wizard is not null)
        {
            RemoveControl(_wizard);
            _wizard = null;
        }

        _unticked.Clear();
        List<RuntimeDependencyStatus> actionable = Actionable();
        // Always a wizard unless an install is running: an overview first, then one page for every
        // component (the up-to-date ones too, so the person sees what is on the machine), and a last
        // page that sums up what Finish does.
        _wizardActive = !_service.IsBusy;
        if (!_wizardActive)
        {
            return;
        }

        _wizard = new Wizard(ThemeService);
        _wizard.AddStep(new WizardStep("Overview", new RuntimeDependencyOverviewPage(_service, ThemeService)));
        foreach (RuntimeDependencyStatus status in _service.Statuses)
        {
            _wizard.AddStep(new WizardStep(status.Name, new RuntimeDependencyStepPage(status.Id, _service, IsTicked, ThemeService)));
        }

        if (actionable.Count > 0)
        {
            _wizard.AddStep(new WizardStep("Install", new RuntimeDependencySummaryPage(_service, TickedIds, ThemeService), ValidateSomethingTicked));
        }

        _wizard.Finished += (_, _) => FinishWizard();
        AddControl(_wizard);
        PlaceWizard();
    }

    private Task<string?> ValidateSomethingTicked() =>
        Task.FromResult<string?>(TickedIds().Count == 0 ? "Nothing is ticked. Go back and tick what to install." : null);

    private void FinishWizard()
    {
        List<string> ticked = TickedIds();
        _wizardActive = false;
        if (_wizard is not null)
        {
            _wizard.IsVisible = false;
        }

        if (ticked.Count == 0)
        {
            Close();
            return;
        }

        if (ticked.Count == Actionable().Count)
        {
            _service.StartInstall();
        }
        else
        {
            _service.StartInstall(ticked);
        }

        StateHasChanged();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        if (IsWizardActive && plain && !_service.IsBusy)
        {
            Wizard wizard = _wizard!;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    await wizard.NextAsync();
                    return true;
                case ConsoleKey.RightArrow:
                    // Right only browses: on the last step "next" is the install, which wants a deliberate Enter.
                    if (!IsOnLastStep)
                    {
                        await wizard.NextAsync();
                    }

                    return true;
                case ConsoleKey.Backspace:
                case ConsoleKey.LeftArrow:
                    wizard.Back();
                    return true;
                case ConsoleKey.Spacebar:
                    // Only a component that has something to install can be left out.
                    if (wizard.CurrentStep?.Content is RuntimeDependencyStepPage page
                        && Actionable().Any(status => status.Id == page.Id)
                        && !_unticked.Remove(page.Id))
                    {
                        _unticked.Add(page.Id);
                    }

                    StateHasChanged();
                    return true;
                case ConsoleKey.S:
                    List<string> ticked = TickedIds();
                    if (ticked.Count > 0)
                    {
                        if (ticked.Count == Actionable().Count)
                        {
                            _service.SkipOffers();
                        }
                        else
                        {
                            _service.SkipOffers(ticked);
                        }
                        Close();
                    }

                    return true;
            }
        }
        else if (keyEvent.ConsoleKey == ConsoleKey.Enter && plain)
        {
            if (!_service.IsBusy)
            {
                Close();
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The rows of the dialog, each with its colour. Built from the service's current snapshot on every draw,
    /// so a status or a log line that changed in the background shows on the next idle repaint.
    /// </summary>
    internal List<(string Text, Color Color)> Rows(int innerWidth, ColorScheme scheme)
    {
        List<(string Text, Color Color)> rows =
        [
            ("Components iTerminal downloads into your data folder, rather than shipping them.", scheme.Foreground),
            (string.Empty, scheme.Foreground),
        ];

        foreach (RuntimeDependencyStatus status in _service.Statuses)
        {
            (string glyph, Color glyphColor) = StateMarker(status, scheme);
            rows.Add(($"{status.Name} - {status.Purpose}", scheme.Foreground));
            rows.Add(($"{glyph} {RuntimeDependencyStepPage.StateText(status)}", glyphColor));
            rows.Add(status.Problem is not null
                ? ($"  {status.Problem}", Color.DarkRed)
                : ($"  {status.Note}", scheme.DisabledForeground));
        }

        // The separator row doubles as the busy indicator: a sweeping block while an install runs.
        rows.Add(_service.IsBusy
            ? (ProgressBar.BuildIndeterminateBar(Math.Max(1, Math.Min(innerWidth, 40)), (int)(Environment.TickCount64 / 90)), Color.DarkYellow)
            : (string.Empty, scheme.Foreground));
        IReadOnlyList<string> log = _service.Log;
        for (int i = Math.Max(0, log.Count - LogRows); i < log.Count; i++)
        {
            rows.Add((log[i], scheme.DisabledForeground));
        }

        for (int i = log.Count; i < LogRows; i++)
        {
            rows.Add((string.Empty, scheme.DisabledForeground));
        }

        return rows.Select(row => (row.Text.Length > innerWidth ? row.Text.Cut(innerWidth) : row.Text, row.Color)).ToList();
    }

    /// <summary>Whether Enter on the wizard's current step would install rather than move on.</summary>
    private bool IsOnLastStep => _wizard is { CurrentStep: { } step } wizard && wizard.Steps.Count > 0 && ReferenceEquals(step, wizard.Steps[^1]);

    internal string Hint => _service.IsBusy
        ? "Installing... Esc:hide this (it keeps going)"
        : IsWizardActive
            ? IsOnLastStep
                ? HasWork
                    ? "Enter:install  Backspace:back  Space:tick  S:don't ask again  Esc:later"
                    : "Enter:close  Backspace:back  Esc:close"
                : HasWork
                    ? "Enter:next  Backspace:back  Space:tick  S:don't ask again  Esc:later"
                    : "Enter:next  Backspace:back  Esc:close"
            : "Enter or Esc:close";

    private static (string Glyph, Color Color) StateMarker(RuntimeDependencyStatus status, ColorScheme scheme) =>
        status.Problem is not null
            ? (Alert.GlyphFor(BadgeKind.Error).ToString(), Color.DarkRed)
            : status.State == RuntimeDependencyState.Installed
                ? ("✓", Color.DarkGreen)
                : ("●", Color.DarkYellow);

    public override void OnIdle()
    {
        // The busy sweep below moves with the clock, so a running install repaints on its own.
        if (_service.IsBusy)
        {
            StateHasChanged();
        }

        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        List<(string Text, Color Color)> rows = IsWizardActive ? [] : Rows(innerWidth, colorScheme);
        // Stops above the hint row: on a screen too short for every row, the last ones are cut, not the hint.
        for (int i = 0; i < rows.Count && i + 1 < ViewSize.Height - 2; i++)
        {
            WriteRow(buffer, colorScheme, innerWidth, i + 1, rows[i].Text, rows[i].Color);
        }

        string hint = Hint.Length <= innerWidth ? Hint : Hint.Cut(innerWidth);
        WriteRow(buffer, colorScheme, innerWidth, (int)ViewSize.Height - 2, hint, colorScheme.DisabledForeground);
    }

    private void WriteRow(IBuffer buffer, ColorScheme colorScheme, int innerWidth, int row, string text, Color color)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
            new Size(innerWidth, 1),
            new ConxelString(text.ExpandRight(innerWidth), color, colorScheme.Background));
    }
}
