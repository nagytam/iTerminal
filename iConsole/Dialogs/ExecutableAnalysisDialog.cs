using System.Globalization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Executables;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Services.Disassembly;

namespace iConsole.Dialogs;

/// <summary>
/// The executable analysis view: what a PE, ELF or Mach-O file is and how hardened it is, as a two-column grid (item, value). The file is
/// parsed, never run, on a background task; the result lands on the next idle pass. Closing the dialog cancels the read and drops the
/// result. A file that cannot be parsed leaves an empty grid and says why on the status line.
/// F5 (or the Disassemble button) swaps the grid for a disassembly listing of the file, built on a background task the same way;
/// F5 again swaps back.
/// </summary>
public class ExecutableAnalysisDialog : Dialog
{
    private const string CloseActionView = " [X]";
    private const string DisassembleActionView = " [F5]";

    /// <summary>The widest value cell; a longer value continues on the following rows.</summary>
    internal const int ValueWidth = 38;

    private readonly IExecutableAnalyzer _analyzer;
    private readonly Func<string, CancellationToken, IReadOnlyList<string>> _disassemble;
    private readonly CsvGrid _grid;
    private readonly TextViewerControl _listing;
    private readonly Label _statusLabel;
    private CancellationTokenSource? _cancellation;
    private Task<ExecutableReport>? _task;
    private int _generation;
    private int _pendingGeneration;
    private CancellationTokenSource? _listingCancellation;
    private Task<IReadOnlyList<string>>? _listingTask;
    private bool _listingReady;
    private bool _listingShown;

    public ExecutableAnalysisDialog(IExecutableAnalyzer analyzer, IThemeService themeService)
        : this(analyzer, ExecutableDisassembly.Build, themeService)
    {
    }

    /// <param name="disassemble">Builds the listing of a file; the default reads the file and disassembles it. A seam for tests.</param>
    public ExecutableAnalysisDialog(
        IExecutableAnalyzer analyzer,
        Func<string, CancellationToken, IReadOnlyList<string>> disassemble,
        IThemeService themeService)
        : base(nameof(ExecutableAnalysisDialog), themeService)
    {
        _analyzer = analyzer;
        _disassemble = disassemble;
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Exe.Grid.Empty", "Nothing to show") };
        _listing = new TextViewerControl(false, themeService) { IsVisible = false };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_grid);
        AddControl(_listing);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The grid, for a test or a caller to read the cells on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>The read in progress, or null when none is running.</summary>
    public Task<ExecutableReport>? AnalysisTask => _task;

    /// <summary>The report on screen, or null before it lands or when the file could not be read.</summary>
    public ExecutableReport? Report { get; private set; }

    /// <summary>The disassembly listing, shown in place of the grid when F5 is pressed.</summary>
    public TextViewerControl Listing => _listing;

    /// <summary>True while the disassembly listing is shown instead of the analysis grid.</summary>
    public bool IsShowingListing => _listingShown;

    /// <summary>The listing build in progress, or null when none is running.</summary>
    public Task<IReadOnlyList<string>>? ListingTask => _listingTask;

    /// <summary>What the status line says: the file and its format, an error, or that the file is being read.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // A panel reuses one dialog for every file, so nothing of the last file may survive.
        Report = null;
        _grid.SetData([], []);
        _statusLabel.Text = string.Empty;
        ResetListing();
        if (Url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        Title = TitleText();
        _statusLabel.Text = UiText.Get("Exe.Status.Reading", "Reading the file…");
        Retire(_cancellation, _task);
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _pendingGeneration = ++_generation;
        string path = Url.Path;
        // The analyzer opens and reads the file, so the whole call runs on a worker thread.
        _task = Task.Run(() => _analyzer.Analyze(path, token), token);
    }

    /// <summary>Puts a finished analysis on screen. A stale or cancelled one is dropped without a word; a failed one says why.</summary>
    public override void OnIdle()
    {
        if (_task is { IsCompleted: true } task && _pendingGeneration == _generation)
        {
            _task = null;
            _cancellation?.Dispose();
            _cancellation = null;
            Land(task);
            StateHasChanged();
        }

        if (LandListing())
        {
            StateHasChanged();
        }

        base.OnIdle();
    }

    /// <summary>Puts a finished listing into the text view. A listing that was dropped (the dialog was closed or reset) has no task left to land.</summary>
    private bool LandListing()
    {
        if (_listingTask is not { IsCompleted: true } task)
        {
            return false;
        }

        _listingTask = null;
        _listingCancellation?.Dispose();
        _listingCancellation = null;
        if (task.IsCanceled)
        {
            return false;
        }

        if (task.Exception is { } exception)
        {
            _listing.TextModel.Lines.ReplaceAll([string.Format(UiText.Get("Exe.Disassembly.Failed", "Disassembly failed: {0}"), exception.GetBaseException().Message)]);
            return true;
        }

        IReadOnlyList<string> lines = task.Result;
        _listing.TextModel.Lines.ReplaceAll(lines.Count > 0 ? lines : [UiText.Get("Exe.Disassembly.None", "No code found to disassemble")]);
        _listingReady = true;
        return true;
    }

    /// <summary>Swaps the analysis grid and the disassembly listing. The listing is built on the first show of a file and kept until the file changes.</summary>
    public void ToggleListing()
    {
        if (Url is null)
        {
            return;
        }

        _listingShown = !_listingShown;
        ApplyListingVisibility();
        if (_listingShown)
        {
            _listing.SetFocus();
            if (!_listingReady && _listingTask is null)
            {
                StartListing(Url.Path);
            }
        }
        else
        {
            _listing.ResetFocus();
            _grid.SetFocus();
        }

        StateHasChanged();
    }

    private void StartListing(string path)
    {
        _listingCancellation = new CancellationTokenSource();
        CancellationToken token = _listingCancellation.Token;
        _listing.TextModel.Lines.ReplaceAll([UiText.Get("Viewer.Disassembling", "Disassembling…")]);
        Func<string, CancellationToken, IReadOnlyList<string>> disassemble = _disassemble;
        // Reading and disassembling the file is blocking work, so it runs on a worker thread like the analysis.
        _listingTask = Task.Run(() => disassemble(path, token), token);
    }

    /// <summary>Drops the listing of the last file, cancelling a build that is still running.</summary>
    private void ResetListing()
    {
        Retire(_listingCancellation, _listingTask);
        _listingCancellation = null;
        _listingTask = null;
        _listingReady = false;
        _listingShown = false;
        _listing.TextModel.Lines.ReplaceAll([]);
        ApplyListingVisibility();
    }

    /// <summary>Shows exactly one of the two views: the grid or the listing.</summary>
    private void ApplyListingVisibility()
    {
        _grid.IsVisible = !_listingShown;
        _listing.IsVisible = _listingShown;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F5
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            ToggleListing();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void Land(Task<ExecutableReport> task)
    {
        if (task.IsCanceled)
        {
            return;
        }

        if (task.Exception is { } exception)
        {
            Exception inner = exception.InnerException ?? exception;
            _statusLabel.Text = inner switch
            {
                OperationCanceledException => _statusLabel.Text,
                ExecutableAnalysisException => inner.Message,
                _ => string.Format(UiText.Get("Exe.Error.Unexpected", "The file could not be analysed: {0}"), inner.Message),
            };
            return;
        }

        ExecutableReport report = task.Result;
        Report = report;
        _grid.SetData([UiText.Get("Exe.Col.Item", "Item"), UiText.Get("Exe.Col.Value", "Value")], BuildRows(report));
        _statusLabel.Text = string.Format(
            UiText.Get("Exe.Status.Summary", "{0}, {1}, {2:N0} bytes"), report.FormatName, report.Architecture, report.FileSize);
    }

    /// <summary>Turns a report into the rows of the grid. Public so a test can read the text without drawing it.</summary>
    public static IReadOnlyList<string[]> BuildRows(ExecutableReport report)
    {
        List<string[]> rows = [];
        string yes = UiText.Get("Exe.Value.Yes", "yes");
        string no = UiText.Get("Exe.Value.No", "no");
        string unknown = UiText.Get("Exe.Value.NA", "n/a or unknown");

        void Add(string item, string value)
        {
            // A long value continues on the rows below it, so nothing is cut off by the column width.
            string rest = value;
            string label = item;
            do
            {
                int take = Math.Min(rest.Length, ValueWidth);
                rows.Add([label, rest[..take]]);
                rest = rest[take..];
                label = string.Empty;
            }
            while (rest.Length > 0);
        }

        void Section(string title)
        {
            if (rows.Count > 0)
            {
                rows.Add([string.Empty, string.Empty]);
            }

            rows.Add(["── " + title + " ──", string.Empty]);
        }

        Section(UiText.Get("Exe.Sec.General", "General"));
        Add(UiText.Get("Exe.Row.Format", "Format"), report.FormatName);
        Add(UiText.Get("Exe.Row.Architecture", "Architecture"), report.Architecture);
        Add(UiText.Get("Exe.Row.Kind", "Kind"), KindText(report.Kind));
        if (report.Subsystem.Length > 0)
        {
            Add(UiText.Get("Exe.Row.Subsystem", "Platform / subsystem"), report.Subsystem);
        }

        if (report.Timestamp is { } built)
        {
            Add(UiText.Get("Exe.Row.Timestamp", "Built"), built.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }

        if (report.Managed)
        {
            Add(UiText.Get("Exe.Row.Managed", "Managed code"), UiText.Get("Exe.Value.Managed", ".NET assembly (CLR)"));
        }

        if (report.EntryPoint.Length > 0)
        {
            Add(UiText.Get("Exe.Row.Entry", "Entry point"), report.EntryPoint);
        }

        Add(UiText.Get("Exe.Row.Size", "File size"), string.Format(CultureInfo.CurrentCulture, "{0:N0} B", report.FileSize));

        Section(UiText.Get("Exe.Sec.Hardening", "Hardening"));
        foreach (ExecutableFlag flag in report.Hardening)
        {
            Add(flag.Name, flag.Enabled is { } enabled ? (enabled ? yes : no) : unknown);
        }

        Section(UiText.Get("Exe.Sec.Imports", "Imports"));
        Add(
            UiText.Get("Exe.Row.Summary", "Summary"),
            report.ImportedSymbols >= 0
                ? string.Format(UiText.Get("Exe.Imports.Functions", "{0} libraries, {1:N0} imported symbols"), report.Imports.Count, report.ImportedSymbols)
                : string.Format(UiText.Get("Exe.Imports.Libraries", "{0} libraries"), report.Imports.Count));
        foreach (ExecutableImport import in report.Imports)
        {
            Add("  " + import.Library, import.Functions >= 0 ? string.Format(UiText.Get("Exe.Imports.FunctionCount", "{0:N0} functions"), import.Functions) : string.Empty);
        }

        Section(UiText.Get("Exe.Sec.Exports", "Exports"));
        Add(
            UiText.Get("Exe.Row.Summary", "Summary"),
            report.ExportedSymbols == 0
                ? UiText.Get("Exe.Exports.None", "none")
                : string.Format(UiText.Get("Exe.Exports.Count", "{0:N0} exported"), report.ExportedSymbols));
        foreach (string export in report.Exports)
        {
            Add("  " + export, string.Empty);
        }

        Section(UiText.Get("Exe.Sec.Sections", "Sections (size in file, entropy, access)"));
        foreach (ExecutableSection section in report.Sections)
        {
            string entropy = section.Entropy is { } e ? e.ToString("0.00", CultureInfo.InvariantCulture) + (section.Sampled ? "*" : string.Empty) : "-";
            string access = (section.Executable ? "x" : "-") + (section.Writable ? "w" : "-");
            Add(section.Name, string.Format(CultureInfo.CurrentCulture, "{0:N0} B  {1}  {2}", section.FileSize, entropy, access));
        }

        Section(UiText.Get("Exe.Sec.Signature", "Signature"));
        if (!report.Signed)
        {
            Add(UiText.Get("Exe.Row.Signature", "Signature"), UiText.Get("Exe.Signature.None", "not signed"));
        }
        else
        {
            Add(
                UiText.Get("Exe.Row.Signature", "Signature"),
                report.Format == ExecutableFormat.PE
                    ? UiText.Get("Exe.Signature.Embedded", "embedded signature, not verified")
                    : UiText.Get("Exe.Signature.Code", "code signature present, not verified"));
            if (report.Format == ExecutableFormat.PE && report.Certificates.Count == 0)
            {
                Add(string.Empty, UiText.Get("Exe.Signature.NoCertificates", "no certificates could be read"));
            }
        }

        foreach (ExecutableCertificate certificate in report.Certificates)
        {
            Add(UiText.Get("Exe.Cert.Subject", "Subject"), certificate.Subject);
            Add(UiText.Get("Exe.Cert.Issuer", "Issuer"), certificate.Issuer);
            Add(
                UiText.Get("Exe.Cert.Valid", "Valid"),
                string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} .. {1:yyyy-MM-dd}", certificate.NotBefore, certificate.NotAfter));
            Add(UiText.Get("Exe.Cert.Thumbprint", "SHA-1"), certificate.Thumbprint);
        }

        Section(UiText.Get("Exe.Sec.Hints", "Hints"));
        if (report.Hints.Count == 0)
        {
            Add(string.Empty, UiText.Get("Exe.Hint.None", "nothing suspicious found"));
        }

        foreach (ExecutableHint hint in report.Hints.DistinctBy(h => (h.Kind, h.Argument)))
        {
            Add(string.Empty, HintText(hint));
        }

        return rows;
    }

    private static string KindText(ExecutableKind kind) => kind switch
    {
        ExecutableKind.Executable => UiText.Get("Exe.Kind.Executable", "Executable"),
        ExecutableKind.PieExecutable => UiText.Get("Exe.Kind.Pie", "Position-independent executable"),
        ExecutableKind.SharedLibrary => UiText.Get("Exe.Kind.Library", "Shared library"),
        ExecutableKind.Object => UiText.Get("Exe.Kind.Object", "Object file"),
        ExecutableKind.Driver => UiText.Get("Exe.Kind.Driver", "Driver"),
        ExecutableKind.Core => UiText.Get("Exe.Kind.Core", "Core dump"),
        ExecutableKind.Bundle => UiText.Get("Exe.Kind.Bundle", "Bundle"),
        _ => UiText.Get("Exe.Kind.Other", "Other"),
    };

    private static string HintText(ExecutableHint hint) => hint.Kind switch
    {
        ExecutableHintKind.HighEntropySection => string.Format(UiText.Get("Exe.Hint.HighEntropy", "Section {0} is close to random (entropy above 7.2): packed, compressed or encrypted"), hint.Argument),
        ExecutableHintKind.PackerSectionName => string.Format(UiText.Get("Exe.Hint.PackerSection", "Section name {0} is one a packer or protector uses"), hint.Argument),
        ExecutableHintKind.WritableExecutableSection => string.Format(UiText.Get("Exe.Hint.Wx", "Section {0} is both writable and executable"), hint.Argument),
        ExecutableHintKind.FewImports => UiText.Get("Exe.Hint.FewImports", "Very few imports, as a packed program has"),
        ExecutableHintKind.PackerSignature => string.Format(UiText.Get("Exe.Hint.PackerSignature", "{0} packer signature found"), hint.Argument),
        ExecutableHintKind.EntropySampled => string.Format(UiText.Get("Exe.Hint.Sampled", "Entropy of {0} comes from its first 32 MB only"), hint.Argument),
        ExecutableHintKind.Malformed => string.Format(UiText.Get("Exe.Hint.Malformed", "The file is damaged or unusual in: {0}"), hint.Argument),
        ExecutableHintKind.ListCut => string.Format(UiText.Get("Exe.Hint.ListCut", "A limit cut the list: {0}"), hint.Argument),
        ExecutableHintKind.TimestampUnreliable => UiText.Get("Exe.Hint.Timestamp", "The build time stamp is zero or in the future, so it is not shown"),
        _ => hint.Argument,
    };

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? UiText.Get("Exe.Title", "Executable analysis"))
        + UiText.Get("Exe.Title.Hint", "  Up/Down:scroll");

    /// <summary>Cancels what is running and drops its result, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        _pendingGeneration = ++_generation;
        Retire(_cancellation, _task);
        _cancellation = null;
        _task = null;
        Retire(_listingCancellation, _listingTask);
        _listingCancellation = null;
        _listingTask = null;
        base.Close();
    }

    /// <summary>Cancels a read that is dropped, and disposes its source only once the read has finished: the running parser still holds the token.</summary>
    private static void Retire(CancellationTokenSource? source, Task? read)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        (read ?? Task.CompletedTask).ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the listing is on demand, so the view that is not wanted is hidden again.
        ApplyListingVisibility();
        if (_listingShown)
        {
            _listing.SetFocus();
        }
        else
        {
            _grid.SetFocus();
        }
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Exe.Button.Disassemble", "Disassemble"), DisassembleActionView, new ToggleListingCommand(this), ThemeService));
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    private sealed class CloseSelfCommand(ExecutableAnalysisDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    private sealed class ToggleListingCommand(ExecutableAnalysisDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.ToggleListing();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        int width = Math.Max(0, (int)ViewSize.Width - 2);
        _grid.Position = new Position(1, 1);
        _grid.ViewSize = new Size(width, Math.Max(1, (int)ViewSize.Height - 4));
        _listing.Position = new Position(1, 1);
        _listing.ViewSize = new Size(width, Math.Max(1, (int)ViewSize.Height - 4));

        _statusLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
