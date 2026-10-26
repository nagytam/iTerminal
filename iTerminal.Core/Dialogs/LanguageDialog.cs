using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Controls;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+F3: the language of the interface. Each language is listed by its own name (Deutsch, Français, ...)
/// so it can be found whatever language is on screen now. ↑/↓ move, Enter or a click applies and closes; the choice
/// is made at once and saved by the localizer, so the next run starts in it.
/// </summary>
public class LanguageDialog : Dialog
{
    private readonly ILocalizer _localizer;
    private readonly InfoLines _lines;
    private int _cursor;

    public LanguageDialog(ILocalizer localizer, IThemeService themeService)
        : base(nameof(LanguageDialog), themeService)
    {
        _localizer = localizer;
        Title = localizer.Get("Language.Title");
        _lines = new InfoLines(themeService);
        AddControl(_lines);
    }

    /// <summary>The languages listed, English first.</summary>
    public IReadOnlyList<LanguageInfo> Languages => _localizer.Languages;

    /// <summary>The language under the cursor.</summary>
    public LanguageInfo Selected => Languages[_cursor];

    /// <summary>Puts the cursor on the language in use and re-reads the title; call before showing the dialog.</summary>
    public void Load()
    {
        _cursor = Math.Max(0, Languages.ToList().FindIndex(l => l.Code == _localizer.LanguageCode));
        Title = _localizer.Get("Language.Title");
        UpdateLines();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(32, Languages.Count + 4));
        }

        _lines.Position = new Position(2, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Languages.Count);
        base.OnInitializeLayout();
        UpdateLines();
    }

    private void UpdateLines()
    {
        List<InfoLines.Line> lines = [];
        for (int i = 0; i < Languages.Count; i++)
        {
            // The arrow is the cursor; the dot is the language in use now.
            string text = $"{(i == _cursor ? "▶" : " ")} {Languages[i].NativeName,-16}{(Languages[i].Code == _localizer.LanguageCode ? "•" : " ")}";
            lines.Add(new(text, i == _cursor ? Color.Yellow : null));
        }

        _lines.Lines = lines;
        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", "Choose a language");
        yield return ("Home/End", "Jump to the first or last language");
        yield return ("Enter", "Use the chosen language");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _cursor = Math.Max(0, _cursor - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.DownArrow:
                    _cursor = Math.Min(Languages.Count - 1, _cursor + 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.Home:
                    _cursor = 0;
                    UpdateLines();
                    return true;
                case ConsoleKey.End:
                    _cursor = Languages.Count - 1;
                    UpdateLines();
                    return true;
                case ConsoleKey.Enter:
                    Apply();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            _cursor = Math.Clamp(_cursor + (mouseEvent.WheelDelta > 0 ? -1 : 1), 0, Languages.Count - 1);
            UpdateLines();
            return true;
        }

        if (mouseEvent.LeftClicked
            && (long)mouseEvent.Position.X >= (long)_lines.AbsolutePosition.X
            && (long)mouseEvent.Position.X < (long)_lines.AbsolutePosition.X + _lines.ViewSize.Width)
        {
            int row = (int)((long)mouseEvent.Position.Y - (long)_lines.AbsolutePosition.Y);
            if (row >= 0 && row < Languages.Count)
            {
                _cursor = row;
                Apply();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Switches the interface to the language under the cursor, which saves it, and closes.</summary>
    public void Apply()
    {
        _localizer.SetLanguage(Selected.Code);
        Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        int width = (int)ViewSize.Width;
        string hints = $" Enter:{_localizer.Get("Common.Apply").ToLower(_localizer.Culture)}  Esc:{_localizer.Get("Common.Close").ToLower(_localizer.Culture)} ";
        string footer = hints.FitHints(Math.Max(0, width - 4));
        string bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
