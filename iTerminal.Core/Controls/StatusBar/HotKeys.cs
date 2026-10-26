using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interop;
using iConsole.Localization;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Dotnet;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Dialogs.PackageManager;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.StatusBar;

public class HotKeys : StatusBarItem
{
    protected bool IsShiftActive { get; set; }
    protected bool IsControlActive { get; set; }
    protected bool IsAltActive { get; set; }

    public HotKeys(
        TaskManagerDialog taskManagerDialog,
        ProcessManagerDialog processManagerDialog,
        FileViewerDialog fileViewerDialog,
        TextFileEditorDialog textFileEditorDialog,
        CreateFolderDialog createFolderDialog,
        CreateFileDialog createFileDialog,
        DeleteDialog deleteDialog,
        RenameDialog renameDialog,
        DiskManagerDialog diskManagerDialog,
        ThemeManagerDialog themeManagerDialog,
        CopyDialog copyDialog,
        HelpDialog helpDialog,
        TerminalDialog terminalDialog,
        AboutDialog aboutDialog,
        SystemInfoDialog systemInfoDialog,
        NetworkManagerDialog networkManagerDialog,
        IPhoneBrowserDialog iPhoneBrowserDialog,
        SettingsDialog settingsDialog,
        GitClientDialog gitClientDialog,
        iTerminal.Core.Dialogs.Calculator.CalculatorDialog calculatorDialog,
        iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog unitConverterDialog,
        PackageManagerDialog packageManagerDialog,
        iTerminal.Core.Dialogs.Documents.DocumentManagerDialog documentManagerDialog,
        IContentModelService contentModelService,
        FolderHistoryService folderHistoryService,
        DriveInfoModel driveInfoModel,
        FolderIndexModel folderIndexModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        ISettingsService settingsService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        IThemeService themeService,
        SolutionViewerDialog? solutionViewerDialog = null,
        iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? musicFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? videoFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? imageFileViewerDialog = null,
        iTerminal.Core.Dialogs.Search.FindFilesDialog? findFilesDialog = null,
        CompressDialog? compressDialog = null,
        ExtractDialog? extractDialog = null,
        CommandPaletteDialog? commandPaletteDialog = null,
        CompareFoldersDialog? compareFoldersDialog = null,
        iTerminal.Core.Services.FileAssociations.FileAssociationRunner? fileAssociations = null,
        DiskUsageDialog? diskUsageDialog = null,
        iTerminal.Core.Services.Elevation.IElevationService? elevationService = null,
        INotificationService? notificationService = null,
        DuplicateFinderDialog? duplicateFinderDialog = null,
        SystemSettingsDialog? systemSettingsDialog = null,
        iTerminal.Core.Dialogs.Stocks.StockBrowserDialog? stockBrowserDialog = null,
        iTerminal.Core.Dialogs.Translator.TranslatorDialog? translatorDialog = null,
        iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog? currencyConverterDialog = null,
        LanguageDialog? languageDialog = null,
        ILocalizer? localizer = null)
        : base(themeService)
    {
        ViewSize = new Size(1, 1);

        var helpDialogCommand = new HelpDialogCommand(helpDialog);
        var terminalDialogCommand = new TerminalDialogCommand(terminalDialog);
        var taskManagerDialogCommand = new TaskManagerDialogCommand(taskManagerDialog);
        var themeManagerDialogCommand = new ThemeManagerDialogCommand(themeManagerDialog);
        AddCommand(new ProcessManagerDialogCommand(processManagerDialog));
        var screenManagerDialogCommand = new ScreenManagerDialogCommand(themeService);
        var viewTextFileCommand = new ViewFileDialogCommand(fileViewerDialog, contentModelService, solutionViewerDialog, musicFileViewerDialog, videoFileViewerDialog, imageFileViewerDialog);
        var editTextFileCommand = new EditTextFileDialogCommand(textFileEditorDialog, contentModelService, fileAssociations);
        var createFolderDialogCommand = new CreateFolderDialogCommand(createFolderDialog, contentModelService);
        var createFileDialogCommand = new CreateFileDialogCommand(createFileDialog, contentModelService);
        var deleteDialogCommand = new DeleteDialogCommand(deleteDialog, contentModelService);
        var renameDialogCommand = new RenameDialogCommand(renameDialog, contentModelService);
        AddCommand(new DiskManagerDialogCommand(diskManagerDialog));
        if (diskUsageDialog is not null)
        {
            AddCommand(new DiskUsageDialogCommand(diskUsageDialog, contentModelService));
        }

        if (duplicateFinderDialog is not null)
        {
            AddCommand(new DuplicateFinderDialogCommand(duplicateFinderDialog, contentModelService));
        }

        if (elevationService is not null)
        {
            AddCommand(new RestartAsAdministratorCommand(elevationService, notificationService));
            AddCommand(new RunAsAdministratorCommand(contentModelService, elevationService, notificationService));
        }
        AddCommand(new IPhoneBrowserDialogCommand(iPhoneBrowserDialog));
        AddCommand(new iTerminal.Core.Commands.Music.MusicPanelsCommand(contentModelService));
        var quitCommand = new QuitCommand();
        var copyDialogCommand = new CopyDialogCommand(copyDialog, contentModelService);
        var aboutDialogCommand = new AboutDialogCommand(aboutDialog);
        var systemInfoDialogCommand = new SystemInfoDialogCommand(systemInfoDialog);
        AddCommand(systemInfoDialogCommand);
        var networkManagerDialogCommand = new NetworkManagerDialogCommand(networkManagerDialog);
        var settingsDialogCommand = new SettingsDialogCommand(settingsDialog);
        var driveSelectorDialogCommand = new FolderSelectorDialogCommand(
            contentModelService,
            folderHistoryService,
            driveInfoModel,
            folderIndexModel, 
            beepService,
            dateTimeProviderService,
            settingsService,
            frequentExecutionGuardService,
            themeService,
            helpDialog);

        var helpDialogButton = new Button("F1-Help", helpDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(9, 1)
        };
        AddControl(helpDialogButton);
        if (commandPaletteDialog is not null)
        {
            var commandPaletteButton = new Button("Shift-F1-Commands", new CommandPaletteDialogCommand(commandPaletteDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(18, 1),
                IsVisible = false,
            };
            AddControl(commandPaletteButton);
        }
        var terminalDialogButton = new Button("F2-Terminal", terminalDialogCommand, themeService)
        {
            Position = new Position(9, 0),
            ViewSize = new Size(13, 1)
        };
        AddControl(terminalDialogButton);
        var viewTextFileButton = new Button("F3-View", viewTextFileCommand, themeService)
        {
            Position = new Position(22, 0),
            ViewSize = new Size(9, 1)
        };
        AddControl(viewTextFileButton);
        var editTextFileButton = new Button("F4-Edit", editTextFileCommand, themeService)
        {
            Position = new Position(31, 0),
            ViewSize = new Size(9, 1)
        };
        AddControl(editTextFileButton);
        var copyDialogButton = new Button("F5-Copy", copyDialogCommand, themeService)
        {
            Position = new Position(40, 0),
            ViewSize = new Size(9, 1)
        };
        AddControl(copyDialogButton);
        var renameDialogButton = new Button("F6-Rename", renameDialogCommand, themeService)
        {
            Position = new Position(49, 0),
            ViewSize = new Size(11, 1)
        };
        AddControl(renameDialogButton);
        var findFilesDialogButton = new Button("F7-Find", new FindFilesDialogCommand(findFilesDialog!), themeService)
        {
            Position = new Position(60, 0),
            ViewSize = new Size(9, 1)
        };
        AddControl(findFilesDialogButton);
        var deleteDialogButton = new Button("F8-Delete", deleteDialogCommand, themeService)
        {
            Position = new Position(71, 0),
            ViewSize = new Size(11, 1)
        };
        AddControl(deleteDialogButton);
        var taskManagerDialogButton = new Button("F9-Tasks", taskManagerDialogCommand, themeService)
        {
            Position = new Position(82, 0),
            ViewSize = new Size(10, 1)
        };
        AddControl(taskManagerDialogButton);
        var quitButton = new Button("F10-Quit", quitCommand, themeService)
        {
            Position = new Position(92, 0),
            ViewSize = new Size(10, 1)
        };
        AddControl(quitButton);
        var screenManagerDialogButton = new Button("F12-Screens", screenManagerDialogCommand, themeService)
        {
            Position = new Position(102, 0),
            ViewSize = new Size(13, 1)
        };
        AddControl(screenManagerDialogButton);

        var createFileDialogButton = new Button("Shift-F4-Create", createFileDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false,
        };
        AddControl(createFileDialogButton);

        if (compareFoldersDialog is not null)
        {
            var compareButton = new Button("Shift-F2-Compare", new CompareFoldersDialogCommand(compareFoldersDialog, contentModelService), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(17, 1),
                IsVisible = false,
            };
            AddControl(compareButton);
        }

        if (extractDialog is not null)
        {
            var extractDialogButton = new Button("Shift-F5-Unpack", new ExtractDialogCommand(extractDialog, contentModelService), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(16, 1),
                IsVisible = false,
            };
            AddControl(extractDialogButton);
        }

        var createFolderDialogButton = new Button("Shift-F7-Folder", createFolderDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(16, 1),
            IsVisible = false,
        };
        AddControl(createFolderDialogButton);

        var settingsDialogButton = new Button("Shift-F8-Settings", settingsDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false
        };
        AddControl(settingsDialogButton);

        var driveSelectorDialogButton = new Button("Ctrl-F1-Folders", driveSelectorDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false
        };
        AddControl(driveSelectorDialogButton);
        // The drives that open in a panel. Ctrl+F3 sits next to F3 (view) because it is the glance
        // that does not leave the listing.
        var previewButton = new Button("Ctrl-F3-Preview", new iTerminal.Core.Commands.Drives.TogglePreviewCommand(contentModelService), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(16, 1),
            IsVisible = false
        };
        AddControl(previewButton);
        var gitDriveButton = new Button("Ctrl-F6-History", new iTerminal.Core.Commands.Drives.GitDriveCommand(contentModelService), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(16, 1),
            IsVisible = false
        };
        AddControl(gitDriveButton);
        var dockerDriveButton = new Button("Ctrl-F9-Docker", new iTerminal.Core.Commands.Drives.DockerDriveCommand(contentModelService), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1),
            IsVisible = false
        };
        AddControl(dockerDriveButton);
        var recycleDriveButton = new Button("Ctrl-F8-Recycle", new iTerminal.Core.Commands.Drives.RecycleDriveCommand(contentModelService), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(16, 1),
            IsVisible = false
        };
        AddControl(recycleDriveButton);

        var networkManagerDialogButton = new Button("Ctrl-F5-Networks", networkManagerDialogCommand, themeService)
        {
            Position = new Position(28, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false
        };
        AddControl(networkManagerDialogButton);

        var gitClientDialogButton = new Button("Ctrl-G-Git", new GitClientDialogCommand(gitClientDialog), themeService)
        {
            Position = new Position(56, 0),
            ViewSize = new Size(11, 1),
            IsVisible = false
        };
        AddControl(gitClientDialogButton);

        var calculatorDialogButton = new Button("Ctrl-A-Calc", new CalculatorDialogCommand(calculatorDialog), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1),
            IsVisible = false
        };
        AddControl(calculatorDialogButton);

        var unitConverterDialogButton = new Button("Ctrl-E-Convert", new UnitConverterDialogCommand(unitConverterDialog), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1),
            IsVisible = false
        };
        AddControl(unitConverterDialogButton);

        var packageManagerDialogButton = new Button("Alt-F8-Packages", new PackageManagerDialogCommand(packageManagerDialog), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(16, 1),
            IsVisible = false
        };
        AddControl(packageManagerDialogButton);

        var documentManagerDialogButton = new Button("Alt-F3-Documents", new DocumentManagerDialogCommand(documentManagerDialog), themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(18, 1),
            IsVisible = false
        };
        AddControl(documentManagerDialogButton);

        if (compressDialog is not null)
        {
            var compressDialogButton = new Button("Alt-F5-Pack", new CompressDialogCommand(compressDialog, contentModelService), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(13, 1),
                IsVisible = false
            };
            AddControl(compressDialogButton);
        }

        var themeManagerDialogButton = new Button("Alt-F6-Themes", themeManagerDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false
        };
        AddControl(themeManagerDialogButton);
        var aboutDialogButton = new Button("Shift-Ctrl-Alt-F1-About", aboutDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
            IsVisible = false
        };
        AddControl(aboutDialogButton);
        var systemInfoDialogButton = new Button("Ctrl-F11-SysInfo", systemInfoDialogCommand, themeService)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(17, 1),
            IsVisible = false
        };
        AddControl(systemInfoDialogButton);

        if (systemSettingsDialog is not null)
        {
            var systemSettingsDialogButton = new Button("Shift-Ctrl-F10-SysSet", new SystemSettingsDialogCommand(systemSettingsDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(17, 1),
                IsVisible = false
            };
            AddControl(systemSettingsDialogButton);
        }

        if (stockBrowserDialog is not null)
        {
            var stockBrowserDialogButton = new Button("Alt-F7-Stocks", new StockBrowserDialogCommand(stockBrowserDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(15, 1),
                IsVisible = false
            };
            AddControl(stockBrowserDialogButton);
        }

        if (translatorDialog is not null)
        {
            var translatorDialogButton = new Button("Shift-Ctrl-F4-Translate", new TranslatorDialogCommand(translatorDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(18, 1),
                IsVisible = false
            };
            AddControl(translatorDialogButton);
        }

        if (currencyConverterDialog is not null)
        {
            var currencyConverterDialogButton = new Button("Shift-Ctrl-F5-Currency", new CurrencyConverterDialogCommand(currencyConverterDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(22, 1),
                IsVisible = false
            };
            AddControl(currencyConverterDialogButton);
        }

        if (languageDialog is not null)
        {
            var languageDialogButton = new Button("Shift-Ctrl-F3-Language", new LanguageDialogCommand(languageDialog), themeService)
            {
                Position = new Position(0, 0),
                ViewSize = new Size(22, 1),
                IsVisible = false
            };
            AddControl(languageDialogButton);
        }

        _localizer = localizer;
        if (localizer is not null)
        {
            // The language can change on any thread; the idle pass re-reads the labels on the UI thread.
            localizer.LanguageChanged += (_, _) => _languageChanged = true;
            LocalizeButtons();
        }
    }

    private readonly ILocalizer? _localizer;
    private volatile bool _languageChanged;

    /// <summary>The English title each button was built with, "F5-Copy": what a missing translation falls back to.</summary>
    private readonly Dictionary<Button, string> _sourceTitles = [];

    /// <summary>
    /// Re-labels the buttons in the localizer's language. A title is the keys it stands for, a dash, and the
    /// label ("Shift-Ctrl-F3-Language"); only the label is translated, from <c>Footer.</c> and the label, and
    /// the row lays itself out again for the new widths the next time it is drawn.
    /// </summary>
    internal void LocalizeButtons()
    {
        if (_localizer is null)
        {
            return;
        }

        foreach (Button button in Controls.AsSnapshot().OfType<Button>())
        {
            if (!_sourceTitles.TryGetValue(button, out string? source))
            {
                source = button.Title;
                _sourceTitles[button] = source;
            }

            int dash = source.LastIndexOf('-');
            if (dash <= 0)
            {
                continue;
            }

            button.Title = _localizer.TryGet("Footer." + source[(dash + 1)..], out string label)
                ? source[..(dash + 1)] + label
                : source;
        }
    }

    /// <summary>
    /// Base layer (no physical modifiers): only bindings that use plain function keys.
    /// When Shift, Ctrl, and/or Alt are held: only bindings whose <see cref="Key"/> requires exactly that modifier set.
    /// </summary>
    private static bool CommandKeyMatchesModifierLayer(Key k, bool shiftHeld, bool controlHeld, bool altHeld)
    {
        if (!shiftHeld && !controlHeld && !altHeld)
            return !k.Shift && !k.Control && !k.Alt;
        return k.Shift == shiftHeld && k.Control == controlHeld && k.Alt == altHeld;
    }

    /// <summary>
    /// Chooses visible hotkey buttons for the current modifier layer, sizes each button to its label (with brackets),
    /// packs them horizontally, and fits the row to the parent width (truncation handled in <see cref="Button.Draw"/>).
    /// </summary>
    public void LayoutHotKeysRow()
    {
        const uint reservedTrailing = 4;
        var budget = Parent is not null && Parent.ViewSize.Width > reservedTrailing
            ? (uint)((int)Parent.ViewSize.Width - (int)reservedTrailing)
            : uint.MaxValue;

        // Each button's visibility is decided first and set once. Setting it twice per layout —
        // shown for its layer, then hidden again when the row is too narrow for it — moved the
        // tree version on every pass, and every move repaints the whole screen from scratch, which
        // lays this row out again: below about 100 columns the renderer never went idle, so nothing
        // that waits for an idle pass ran at all (dialog lists stayed empty, progress windows open).
        var all = new List<Button>();
        var visible = new List<Button>();
        foreach (var control in Controls.AsSnapshot())
        {
            if (control is not Button button)
                continue;
            all.Add(button);
            var command = button.Commands.FirstOrDefault();
            var match = command?.Key is Key k
                && CommandKeyMatchesModifierLayer(k, IsShiftActive, IsControlActive, IsAltActive);
            if (match == true)
                visible.Add(button);
        }

        if (visible.Count == 0)
        {
            foreach (var button in all)
            {
                button.IsVisible = false;
            }
            ViewSize = new Size(1, 1);
            return;
        }

        var naturals = visible.Select(b => Math.Max(2, b.Title.Length + 2)).ToList();
        var sum = naturals.Sum();
        List<int> widths = (uint)sum <= budget
            ? naturals
            : FitTitlesToBudget(visible.Select(b => b.Title).ToList(), naturals, (int)budget);

        var shown = new HashSet<Button>();
        uint x = 0;
        for (var i = 0; i < visible.Count; i++)
        {
            if (widths[i] <= 0)
            {
                // Left out to keep the others readable; the key itself still works.
                continue;
            }
            shown.Add(visible[i]);
            var w = (uint)widths[i];
            visible[i].ViewSize = new Size(w, 1);
            visible[i].Position = new Position(x, 0);
            x += w;
        }

        foreach (var button in all)
        {
            button.IsVisible = shown.Contains(button);
        }

        ViewSize = new Size(Math.Max(1, x), 1);
    }

    /// <summary>A word shorter than this reads as a typo ("Hel", "Ter"); below it, buttons go first.</summary>
    private const int ReadableWordLength = 4;

    /// <summary>
    /// The buttons that give way first when the row is too narrow, most expendable first: the screen
    /// list, the task list, the terminal (Ctrl+O opens a real shell anyway) and the editor. Each key still works;
    /// it is only not advertised. Anything else goes from the right — except <see cref="KeptLast"/>.
    /// </summary>
    private static readonly string[] ExpendableFirst = ["F12-", "F9-", "F11-", "F2-", "F4-"];

    /// <summary>Help and Quit: the two keys someone new to the app has to be able to find.</summary>
    private static readonly string[] KeptLast = ["F1-", "F10-"];

    /// <summary>
    /// Fits the labels into <paramref name="budget"/> the way a reader can still use them, in this
    /// order: cut only the longest words, to a common length, keeping short words whole and the key
    /// ("F2-", "Ctrl-F5-") always; once that would leave words shorter than
    /// <see cref="ReadableWordLength"/>, leave out the least important buttons instead (at most half
    /// of them); and only on a screen too narrow even for that, cut the remaining words further.
    /// A width of 0 means the button is left out.
    ///
    /// <para>The bar used to scale every button by the same factor, which cut the short words as
    /// hard as the long ones — "[F1-Hel][F2-Termi][F3-Vie][F4-Edi]" at 100 columns — and, on a narrow
    /// screen, the keys themselves.</para>
    /// </summary>
    internal static List<int> FitTitlesToBudget(IReadOnlyList<string> titles, IReadOnlyList<int> naturals, int budget)
    {
        var parts = titles
            .Select(title =>
            {
                var dash = title.LastIndexOf('-');
                return dash < 0 || dash == title.Length - 1
                    ? (Key: title.Length, Word: 0)
                    : (Key: dash + 1, Word: title.Length - dash - 1);
            })
            .ToList();
        var longestWord = parts.Count == 0 ? 0 : parts.Max(p => p.Word);
        var readable = Math.Min(ReadableWordLength, longestWord);
        var included = Enumerable.Repeat(true, parts.Count).ToList();

        List<int> Widths(int wordLength) =>
            parts.Select((p, i) => included[i] ? 2 + p.Key + Math.Min(p.Word, wordLength) : 0).ToList();

        // 1. Shorten the longest words, never below a readable length.
        for (var wordLength = longestWord; wordLength >= readable; wordLength--)
        {
            if (Widths(wordLength).Sum() <= budget)
            {
                return Widths(wordLength);
            }
        }

        // 2. Leave buttons out, the expendable ones first, keeping at least half of them.
        foreach (var index in DropOrder(titles))
        {
            if (included.Count(i => i) <= (parts.Count + 1) / 2)
            {
                break;
            }
            included[index] = false;
            if (Widths(readable).Sum() <= budget)
            {
                return Widths(readable);
            }
        }

        // 3. Too narrow even so: the buttons that are left, their words shortened as far as one letter.
        for (var wordLength = readable - 1; wordLength >= 1; wordLength--)
        {
            if (Widths(wordLength).Sum() <= budget)
            {
                return Widths(wordLength);
            }
        }

        return FitWidthsToBudget(naturals, budget);
    }

    private static List<int> DropOrder(IReadOnlyList<string> titles)
    {
        bool Starts(int i, string prefix) => titles[i].StartsWith(prefix, StringComparison.Ordinal);
        var order = new List<int>();
        foreach (var prefix in ExpendableFirst)
        {
            order.AddRange(Enumerable.Range(0, titles.Count).Where(i => !order.Contains(i) && Starts(i, prefix)));
        }
        order.AddRange(Enumerable.Range(0, titles.Count).Reverse()
            .Where(i => !order.Contains(i) && !KeptLast.Any(prefix => Starts(i, prefix))));
        order.AddRange(Enumerable.Range(0, titles.Count).Reverse().Where(i => !order.Contains(i)));
        return order;
    }

    private static List<int> FitWidthsToBudget(IReadOnlyList<int> naturals, int budget)
    {
        var n = naturals.Count;
        var sum = naturals.Sum();
        var w = new List<int>(n);
        for (var i = 0; i < n; i++)
        {
            var scaled = (int)Math.Round(naturals[i] * (double)budget / sum);
            w.Add(Math.Max(1, scaled));
        }

        var total = w.Sum();
        for (var guard = 0; guard < 4096 && total != budget; guard++)
        {
            if (total > budget)
            {
                var j = -1;
                var maxW = -1;
                for (var i = 0; i < n; i++)
                {
                    if (w[i] <= 1)
                        continue;
                    if (w[i] > maxW)
                    {
                        maxW = w[i];
                        j = i;
                    }
                }

                if (j < 0)
                    break;
                w[j]--;
                total--;
            }
            else
            {
                w[^1]++;
                total++;
            }
        }

        return w;
    }

    public override void Draw(IBuffer buffer)
    {
        foreach (var control in Controls.AsSnapshot().Where(c => c.IsVisible))
            control.Draw(buffer);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutHotKeysRow();
    }

    public override void OnIdle()
    {
        if (_languageChanged)
        {
            _languageChanged = false;
            LocalizeButtons();
            StateHasChanged();
        }

        if (OperatingSystem.IsWindows())
        {
            var shift = WindowsPhysicalKeyboard.IsShiftHeld();
            var control = WindowsPhysicalKeyboard.IsControlHeld();
            var alt = WindowsPhysicalKeyboard.IsAltHeld();
            if (shift != IsShiftActive || control != IsControlActive || alt != IsAltActive)
            {
                IsShiftActive = shift;
                IsControlActive = control;
                IsAltActive = alt;
                StateHasChanged();
            }
        }

        base.OnIdle();
    }
}
