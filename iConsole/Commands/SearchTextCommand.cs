using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Model;

namespace iConsole.Commands;

public class SearchTextCommand(ISearchModel searchModel, ITextModel textModel, IScrollableModel scrollableModel) 
    : Command
{
    private ISearchModel _searchModel { get; set; } = searchModel;
    private ITextModel _textModel { get; set; } = textModel;
    private IScrollableModel _scrollableModel { get; set; } = scrollableModel;
    public override string Title => "Search";
    
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return true;
    }

    public override Task ExecuteAsync(object? arg)
    {
        var searchTextDialog = Control?.Parent as SearchTextDialog;
        string search = searchTextDialog?.SearchValue ?? string.Empty;
        bool useRegex = searchTextDialog?.UseRegex ?? false;
        bool matchCase = searchTextDialog?.MatchCase ?? true;
        _searchModel.Search = search;
        _searchModel.UseRegex = useRegex;
        _searchModel.MatchCase = matchCase;

        var hits = SearchEngine.FindAll(_textModel.Lines, search, useRegex, matchCase, out var error);
        if (error is not null)
        {
            // The dialog stays open with the reason, so the pattern can be mended.
            searchTextDialog?.ShowError(error);
            return Task.CompletedTask;
        }

        _searchModel.SetHits(hits);
        if (_searchModel.Hits.Count > 0)
        {
            var hit = _searchModel.Hits.First();
            _scrollableModel.CursorPosition = hit;
            _scrollableModel.ScrollToVisible();
            searchTextDialog?.Close();
        }
        return Task.CompletedTask;
    }
}
