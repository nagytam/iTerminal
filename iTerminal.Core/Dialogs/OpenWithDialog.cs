using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// "Open with…" (Shift+F3) and "Edit with…" (Ctrl+F4): the programs that apply to the file under the
/// cursor, the default first, and under them what the app does when none is chosen — the system's
/// own association, or the built-in editor. ↑/↓ and Enter open the file with the row; Ins adds a
/// program, F4 changes one, Del removes one and Space makes one the default, which is the program
/// Enter (or F4) on such a file uses from then on.
///
/// <para>Opening closes the dialog <em>first</em> and only starts the program
/// (<see cref="FileAssociationRunner"/>): a terminal program takes the whole screen, and a dialog
/// still attached would be what the panels come back under.</para>
/// </summary>
public class OpenWithDialog : Dialog
{
    /// <summary>A row of the list: a saved program, or one of the choices that are always there.</summary>
    public sealed record Choice(string Title, string Detail, FileAssociation? Association, bool IsDefault = false);

    private readonly FileAssociationRunner _runner;
    private readonly FileAssociationDialog _associationDialog;
    private readonly ChoiceList _list;
    private long _loadedVersion = -1;
    private Choice? _pendingRemoval;

    public OpenWithDialog(
        FileAssociationRunner runner,
        FileAssociationDialog associationDialog,
        IThemeService themeService)
        : base(nameof(OpenWithDialog), themeService)
    {
        _runner = runner;
        _associationDialog = associationDialog;
        Title = iConsole.Localization.UiText.Get("Dialog.OpenWith", "Open with");
        _list = new ChoiceList(themeService);
        AddControl(_list);
    }

    public FileAssociationKind Kind { get; private set; }

    /// <summary>The local file to open.</summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Opens the file in the app's own editor — the last row of "Edit with…" when the file is one
    /// the editor takes. Supplied by the command, which is what has the editor and a control to
    /// show it from.
    /// </summary>
    public Func<Task>? BuiltInEditor { get; private set; }

    public IReadOnlyList<Choice> Choices => _list.Items;

    public Choice? Selected => _list.SelectedItem;

    /// <summary>The y/n question in the footer, or empty.</summary>
    public string Question => _pendingRemoval is { } choice ? $"Remove {choice.Title} from the list? (y/n)" : string.Empty;

    /// <summary>Sets what the dialog is about; call before showing it.</summary>
    public void Load(FileAssociationKind kind, string filePath, Func<Task>? builtInEditor = null)
    {
        Kind = kind;
        FilePath = filePath;
        BuiltInEditor = builtInEditor;
        _pendingRemoval = null;
        Reload(selectId: null);
    }

    private string FileName => Path.GetFileName(FilePath);

    private void Reload(string? selectId)
    {
        _loadedVersion = _runner.Associations.Version;

        var choices = new List<Choice>();
        var associations = _runner.Associations.GetFor(FileName, Kind);
        for (var i = 0; i < associations.Count; i++)
        {
            var association = associations[i];
            var detail = association.Command + (association.InTerminal ? "  (in the terminal)" : string.Empty);
            choices.Add(new Choice(association.Name, detail, association, IsDefault: i == 0));
        }

        if (Kind == FileAssociationKind.Open)
        {
            choices.Add(new Choice("System default", "Whatever the system opens this kind of file with.", null));
        }
        else if (BuiltInEditor is not null)
        {
            choices.Add(new Choice("iTerminal editor", "The built-in text editor.", null));
        }

        _list.SetItems(choices, choices.FindIndex(c => selectId is not null && c.Association?.Id == selectId));
        StateHasChanged();
    }

    public override async Task OnInitializeAsync()
    {
        Title = (Kind == FileAssociationKind.Edit ? "Edit " : "Open ") + FileName + " with";
        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = Math.Clamp((int)Parent.ViewSize.Width - 8, 40, 90);
            // A fixed height, not one that follows the list: the list changes while the dialog is
            // open, and a dialog that shrank would leave its old rows on the layer.
            var height = Math.Clamp((int)Parent.ViewSize.Height - 6, 9, 16);
            CenterInParent(new Size(width, height));
        }

        _list.Position = new Position(1, 1);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 4));
        base.OnInitializeLayout();
    }

    /// <summary>A program added or changed in the form over this dialog shows up when the form closes: the idle pass checks.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        if (_runner.Associations.Version != _loadedVersion)
        {
            Reload(Selected?.Association?.Id ?? _runner.Associations.GetFor(FileName, Kind).FirstOrDefault()?.Id);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_pendingRemoval is not null)
        {
            yield return ("Y", "Remove the program from the list");
            yield return ("Any other key", "Keep the program");
            yield break;
        }

        yield return ("Up/Down", "Choose a program");
        yield return ("PageUp/PageDown", "Move a page at a time");
        yield return ("Home/End", "Jump to the first or last program");
        yield return ("Enter", "Open the file with the chosen program");
        yield return ("Insert", "Add a program");
        yield return ("F4", "Change the chosen program");
        yield return ("Delete", "Remove the chosen program (asks first)");
        yield return ("Space", "Make the chosen program the default");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_pendingRemoval is { } removal)
        {
            // Any key answers the question; only y removes.
            _pendingRemoval = null;
            if (keyEvent.ConsoleKey == ConsoleKey.Y && removal.Association is { } association)
            {
                _runner.Associations.Remove(association.Id);
                Reload(selectId: null);
            }

            StateHasChanged();
            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _list.Move(-1);
                    StateHasChanged();
                    return true;
                case ConsoleKey.DownArrow:
                    _list.Move(1);
                    StateHasChanged();
                    return true;
                case ConsoleKey.Home:
                    _list.Move(-_list.Items.Count);
                    StateHasChanged();
                    return true;
                case ConsoleKey.End:
                    _list.Move(_list.Items.Count);
                    StateHasChanged();
                    return true;
                case ConsoleKey.PageUp:
                    _list.Move(-_list.PageSize);
                    StateHasChanged();
                    return true;
                case ConsoleKey.PageDown:
                    _list.Move(_list.PageSize);
                    StateHasChanged();
                    return true;
                case ConsoleKey.Enter:
                    await OpenSelectedAsync();
                    return true;
                case ConsoleKey.Insert:
                    await AddAsync();
                    return true;
                case ConsoleKey.F4:
                    await ChangeSelectedAsync();
                    return true;
                case ConsoleKey.Delete:
                    if (Selected?.Association is not null)
                    {
                        _pendingRemoval = Selected;
                        StateHasChanged();
                    }
                    return true;
                case ConsoleKey.Spacebar:
                    MakeSelectedDefault();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The wheel moves the choice three rows; a click on a row opens the file with it, as Enter does. While the
    /// remove question is up the mouse leaves it alone: only a key answers it.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_pendingRemoval is null)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                _list.Move(mouseEvent.WheelDelta > 0 ? -3 : 3);
                StateHasChanged();
                return true;
            }

            if (mouseEvent.LeftClicked && _list.TryGetIndexAt(mouseEvent.Position, out var index))
            {
                _list.Select(index);
                await OpenSelectedAsync();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Closes the dialog and opens the file with the selected row. False when there is no row.</summary>
    public async Task<bool> OpenSelectedAsync()
    {
        if (Selected is not { } choice)
        {
            return false;
        }

        var builtInEditor = BuiltInEditor;
        Close();

        if (choice.Association is { } association)
        {
            _ = _runner.Start(association, FilePath);
        }
        else if (Kind == FileAssociationKind.Open)
        {
            _ = _runner.StartSystemDefault(FilePath);
        }
        else if (builtInEditor is not null)
        {
            await builtInEditor();
        }

        return true;
    }

    /// <summary>Opens the form for a new program, filled in for this file's extension.</summary>
    public async Task AddAsync()
    {
        _associationDialog.LoadNew(Kind, FileAssociation.PatternFor(FileName));
        await ShowDialogAsync(_associationDialog);
    }

    /// <summary>Opens the form on the selected program; the rows that are always there cannot be changed.</summary>
    public async Task ChangeSelectedAsync()
    {
        if (Selected?.Association is { } association)
        {
            _associationDialog.LoadExisting(association);
            await ShowDialogAsync(_associationDialog);
        }
    }

    public void MakeSelectedDefault()
    {
        if (Selected?.Association is { } association && _runner.Associations.MakeDefault(association.Id))
        {
            Reload(association.Id);
        }
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var inner = Math.Max(0, width - 2);

        // What the chosen row runs, above the bottom edge — or the question a Del is waiting on.
        var question = Question;
        var detail = question.Length > 0 ? question : Selected?.Detail ?? string.Empty;
        var detailText = (" " + detail).Shrink(inner)?.ExpandRight(inner) ?? string.Empty;
        var detailLine = new ConxelString(detailText, question.Length > 0 ? colorScheme.Error : colorScheme.DisabledForeground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2), new Size(detailLine.Count, 1), detailLine);

        var rule = new ConxelString(Border.Horizontal.Repeat(inner), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 3), new Size(rule.Count, 1), rule);

        var hints = Selected?.Association is not null
            ? " Enter:open  Ins:add  F4:change  Del:remove  Space:default  Esc:close "
            : " Enter:open  Ins:add  Esc:close ";
        var footer = hints.FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    /// <summary>The rows: a program's name on the left, "default" on the right of the one Enter uses, the chosen row highlighted.</summary>
    private sealed class ChoiceList(IThemeService themeService) : Control(themeService)
    {
        private const string DefaultMark = "default ";

        private int _top;

        public IReadOnlyList<Choice> Items { get; private set; } = [];

        public int SelectedIndex { get; private set; }

        public Choice? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        /// <summary>The row drawn under <paramref name="position"/>, if there is one.</summary>
        public bool TryGetIndexAt(Position position, out int index)
        {
            long column = (long)position.X - (long)AbsolutePosition.X;
            long row = (long)position.Y - (long)AbsolutePosition.Y;
            index = -1;
            if (column < 0 || column >= ViewSize.Width || row < 0 || row >= ViewSize.Height)
            {
                return false;
            }

            index = _top + (int)row;
            return index < Items.Count;
        }

        public void Select(int index)
        {
            if (index >= 0 && index < Items.Count)
            {
                SelectedIndex = index;
                StateHasChanged();
            }
        }

        public void SetItems(IReadOnlyList<Choice> items, int selectedIndex)
        {
            Items = items;
            SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, items.Count - 1);
            _top = 0;
            Move(0);
        }

        public void Move(int step)
        {
            if (Items.Count == 0)
            {
                StateHasChanged();
                return;
            }

            SelectedIndex = Math.Clamp(SelectedIndex + step, 0, Items.Count - 1);
            var height = Math.Max(1, (int)ViewSize.Height);
            if (SelectedIndex < _top)
            {
                _top = SelectedIndex;
            }
            else if (SelectedIndex >= _top + height)
            {
                _top = SelectedIndex - height + 1;
            }

            StateHasChanged();
        }

        public override void Draw(IBuffer buffer)
        {
            var colorScheme = ThemeService.CurrentColorScheme;
            var width = (int)ViewSize.Width;
            var height = (int)ViewSize.Height;
            for (var row = 0; row < height; row++)
            {
                var index = _top + row;
                var text = index == 0 && Items.Count == 0 ? " No program yet - Ins adds one." : string.Empty;
                Color foreground = colorScheme.Foreground, background = colorScheme.Background;
                if (index < Items.Count)
                {
                    var choice = Items[index];
                    var mark = choice.IsDefault ? DefaultMark : string.Empty;
                    var titleWidth = Math.Max(0, width - 1 - mark.Length);
                    text = " " + (choice.Title.Shrink(titleWidth - 1) ?? string.Empty).ExpandRight(titleWidth) + mark;
                    if (index == SelectedIndex)
                    {
                        foreground = colorScheme.SelectedFocusedForeground;
                        background = colorScheme.SelectedFocusedBackground;
                    }
                }

                // Every cell of the rectangle, every time: a shorter list must not leave old rows behind.
                var line = new ConxelString(text.ExpandRight(width)[..Math.Max(0, width)], foreground, background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
            }
        }
    }
}
