using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Screens;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Lists the desktop's screens, marking the one on show. The desktop runs one screen at a time and
/// has no way to switch, so the list is for looking at — which the hint row says, rather than
/// leaving Enter to do nothing.
/// </summary>
public class ScreenManagerDialog : Dialog
{
    internal const string Hint = "The open screens (the desktop shows one at a time).  Esc:close";

    private readonly ConcurrentObservableCollection<IScreen> _screens = [];
    private ListBox<IScreen> _listBox { get; set; }

    public ScreenManagerDialog(IThemeService themeService)
        : base(nameof(ScreenManagerDialog), themeService)
    {
        Title = "Screen manager";

        var listModel = new ListBoxModel<IScreen>(_screens)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<IScreen>(listModel, themeService)
        {
            EmptyText = " No screens.",
        };
        AddControl(_listBox);
    }

    internal IReadOnlyList<string> RowsForTest => [.. _listBox.ListModel.ListItems.Select(i => i.Value)];

    private ListItem<IScreen> TransformItem(IScreen screen, CancellationToken cancellationToken)
    {
        var current = ReferenceEquals(screen, GetRootControl()) ? "  (current)" : string.Empty;
        return new ListItem<IScreen>(screen)
        {
            Value = " " + DisplayName(screen.Name) + current,
            IsEnabled = true
        };
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        // A screen that has not joined a desktop yet has no Desktop: that is an empty list, not a
        // NullReferenceException on the key that opened the dialog.
        var desktop = (GetRootControl() as IScreen)?.Desktop;
        _screens.ReplaceAll(desktop is null ? [] : [.. desktop.Screens]);
        _listBox.UpdateModel();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(Math.Max(0, (int)ViewSize.Width - 2), Math.Max(0, (int)ViewSize.Height - 3));

        base.OnInitializeLayout();
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2),
            new Size(innerWidth, 1),
            new ConxelString((" " + Hint).FitHints(innerWidth).ExpandRight(innerWidth), colorScheme.DisabledForeground, colorScheme.Background));
    }

    /// <summary>
    /// A screen's name as a person would say it: a screen is named after its type, and the row used
    /// to read "iTerminal.Core.Screens.TerminalScreen". The namespace and the "Screen" suffix go, and
    /// the words are spaced: "Terminal".
    /// </summary>
    internal static string DisplayName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "(unnamed)";
        }
        var name = typeName[(typeName.LastIndexOf('.') + 1)..];
        if (name.Length > "Screen".Length && name.EndsWith("Screen", StringComparison.Ordinal))
        {
            name = name[..^"Screen".Length];
        }
        var spaced = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
            {
                spaced.Append(' ');
            }
            spaced.Append(name[i]);
        }
        return spaced.ToString();
    }
}