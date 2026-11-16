using iConsole.Localization;
﻿using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class SearchResultsIndicator : StatusBarItem
{
    private ISearchModel _searchModel { get; set; }
    private IScrollableModel? _scrollableModel { get; set; }

    public SearchResultsIndicator(
        ISearchModel searchModel,
        IThemeService themeService,
        IScrollableModel? scrollableModel = null)
        : base(themeService)
    {
        _searchModel = searchModel;
        _scrollableModel = scrollableModel;
        _scrollableModel?.PropertyChanged += OnPropertyChanged;
        _searchModel.PropertyChanged += OnPropertyChanged;
        Name = nameof(SearchResultsIndicator);
        ViewSize = new Size(10, 1);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    /// <summary>
    /// "Hit n/N": n counts the hits up to the cursor, so the hit the cursor is on is the one it names, and a cursor
    /// between two hits names the one before it. Without a cursor to go by, the total alone.
    /// </summary>
    public string HitText()
    {
        var hits = _searchModel.Hits.AsSnapshot();
        if (hits.Count == 0 && string.IsNullOrEmpty(_searchModel.Search))
        {
            // Nothing has been searched for, so there is no count to show. "Hit 0/0" read as a failed search.
            return string.Empty;
        }
        if (_scrollableModel is null)
        {
            return string.Format(UiText.Get("Status.Search", "Search: {0}"), hits.Count);
        }

        var cursor = _scrollableModel.CursorPosition;
        int upToCursor = hits.Count(h => h.Y < cursor.Y || (h.Y == cursor.Y && h.X <= cursor.X));
        return string.Format(UiText.Get("Status.Hit", "Hit {0}/{1}"), upToCursor, hits.Count);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var text = HitText();
        // The row is not cleared between frames, so an empty count is painted as blanks in the bar's own colours.
        var s = text.Length == 0
            ? new ConxelString(new string(' ', (int)ViewSize.Width), ThemeService.CurrentColorScheme.Foreground, ThemeService.CurrentColorScheme.Background)
            : new ConxelString(text, Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
