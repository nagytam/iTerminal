using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Images;

namespace iTerminal.Core.Dialogs;

/// <summary>Where <see cref="StripMetadataDialog"/> is: choosing, waiting for the second Enter, stripping, or over.</summary>
public enum StripPhase
{
    Choosing,
    Armed,
    Running,
    Finished,
}

/// <summary>What one strip run did: files changed, files that had nothing to remove, and the files it could not change.</summary>
public sealed record StripResult(int Stripped, int Unchanged, IReadOnlyList<string> Failures);

/// <summary>
/// Strips the metadata from the marked JPEG and PNG files (or the one under the cursor) in one run
/// (<see cref="ImageMetadataWriter.Strip"/>): the camera, GPS position, author and comments go, the picture stays.
///
/// <list type="bullet">
/// <item><b>The files are overwritten, so Enter asks twice.</b> The first Enter says what will be removed; the second
/// strips; any other key withdraws. The same two-step confirmation as the secure wipe.</item>
/// <item><b>Only JPEG and PNG are touched.</b> Other files in the selection are counted and skipped, not refused.</item>
/// <item><b>The run is on a background task</b>; closing the dialog does not stop the files already done, and the panels are
/// re-read when it finishes.</item>
/// </list>
/// </summary>
public class StripMetadataDialog : Dialog
{
    private readonly IContentModelService _contentModelService;
    private readonly InfoLines _info;
    private int _skipped;
    private volatile StripResult? _result;

    public StripMetadataDialog(IContentModelService contentModelService, IThemeService themeService)
        : base(nameof(StripMetadataDialog), themeService)
    {
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.StripMetadata", "Strip metadata");
        _info = new InfoLines(themeService);
        AddControl(_info);
    }

    /// <summary>The JPEG and PNG files that will be stripped.</summary>
    public IReadOnlyList<string> Files { get; private set; } = [];

    public StripPhase Phase { get; private set; } = StripPhase.Choosing;

    /// <summary>The outcome, once <see cref="Phase"/> is <see cref="StripPhase.Finished"/>.</summary>
    public StripResult? Result => _result;

    /// <summary>The strip started by the second Enter; nothing waits on it, it exists so a test can.</summary>
    public Task StripTask { get; private set; } = Task.CompletedTask;

    /// <summary>The lines as drawn.</summary>
    public string Details => _info.Text;

    /// <summary>Sets what to strip; call before showing the dialog.</summary>
    public void Load(IEnumerable<string> paths)
    {
        var all = paths.ToList();
        Files = all.Where(path => ImageMetadataWriter.CanWrite(path) && File.Exists(path)).ToList();
        _skipped = all.Count - Files.Count;
        Phase = StripPhase.Choosing;
        _result = null;
    }

    /// <summary>The first Enter: says what is about to happen and waits for the second.</summary>
    public void Arm()
    {
        if (Phase == StripPhase.Choosing && Files.Count > 0)
        {
            Phase = StripPhase.Armed;
            UpdateLines();
        }
    }

    /// <summary>Any key other than Enter: back to the first step.</summary>
    public void Withdraw()
    {
        if (Phase == StripPhase.Armed)
        {
            Phase = StripPhase.Choosing;
            UpdateLines();
        }
    }

    /// <summary>The second Enter: strips the files on a background task.</summary>
    public void Confirm()
    {
        if (Phase != StripPhase.Armed)
        {
            return;
        }

        Phase = StripPhase.Running;
        var files = Files;
        StripTask = Task.Run(() =>
        {
            var stripped = 0;
            var unchanged = 0;
            var failures = new List<string>();
            foreach (var file in files)
            {
                try
                {
                    if (ImageMetadataWriter.Strip(file))
                    {
                        stripped++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
            }

            // The idle pass sees the result and moves the dialog to Finished on the thread that owns the controls.
            _result = new StripResult(stripped, unchanged, failures);
        });
        UpdateLines();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (Phase == StripPhase.Armed)
        {
            yield return ("Enter", "Confirm: the metadata is removed from the files");
            yield return ("Any other key", "Withdraw");
        }
        else if (Phase == StripPhase.Choosing)
        {
            yield return ("Enter", "Arm the removal (nothing is changed yet)");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            if (Phase == StripPhase.Choosing)
            {
                Arm();
            }
            else
            {
                Confirm();
            }

            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey != ConsoleKey.Escape && Phase == StripPhase.Armed)
        {
            Withdraw();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 4, 50, 78), 14));
        }

        _info.Position = new Position(2, 1);
        _info.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Math.Max(1, ViewSize.Height - 3));
        base.OnInitializeLayout();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        UpdateLines();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (Phase == StripPhase.Running && _result is not null)
        {
            Phase = StripPhase.Finished;
            PanelRefreshHelper.RefreshLocalPanels(_contentModelService);
        }

        UpdateLines();
    }

    private void UpdateLines()
    {
        var colors = ThemeService.CurrentColorScheme;
        var lines = new List<InfoLines.Line>();
        lines.Add(new($"Strip  {Files.Count} JPEG/PNG file{(Files.Count == 1 ? string.Empty : "s")}"
            + (_skipped > 0 ? $", {_skipped} other skipped" : string.Empty)));

        switch (Phase)
        {
            case StripPhase.Choosing:
                lines.Add(default);
                lines.Add(new("Camera, GPS position, author, copyright and comments are removed."));
                lines.Add(new("The picture, its colour profile and its layout are kept.", colors.DisabledForeground));
                lines.Add(default);
                lines.Add(new("The files are overwritten; the removed text cannot be recovered.", colors.Error));
                lines.Add(default);
                lines.Add(new(Files.Count == 0 ? "Nothing to strip: mark JPEG or PNG files first." : "Enter:continue", colors.DisabledForeground));
                break;
            case StripPhase.Armed:
                lines.Add(default);
                lines.Add(new($"This overwrites {Files.Count} file{(Files.Count == 1 ? string.Empty : "s")} for good. Press Enter again to strip,", colors.Error));
                lines.Add(new("any other key to go back.", colors.Error));
                break;
            case StripPhase.Running:
                lines.Add(default);
                lines.Add(new("Stripping…", colors.DisabledForeground));
                break;
            case StripPhase.Finished:
                if (_result is { } result)
                {
                    lines.Add(default);
                    lines.Add(new($"Stripped {result.Stripped}, unchanged {result.Unchanged}" + (result.Failures.Count > 0 ? $", failed {result.Failures.Count}" : string.Empty)));
                    foreach (var failure in result.Failures.Take(3))
                    {
                        lines.Add(new(failure.Shrink(Math.Max(16, (int)_info.ViewSize.Width)) ?? string.Empty, colors.Error));
                    }
                }

                lines.Add(default);
                lines.Add(new("Esc:close", colors.DisabledForeground));
                break;
        }

        _info.Lines = lines;
    }
}
