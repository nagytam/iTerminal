using iConsole.Data;
using iConsole.Dialogs;
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
        _searchModel.Search = search;

        _searchModel.Hits.Clear();
        int y = 0;
        foreach (var line in _textModel.Lines)
        {
            int x = 0;
            while (search.Length > 0 && (x = line.IndexOf(search, x)) != -1)
            {
                _searchModel.Hits.Add(new Position(x, y));
                x++;
            }
            y++;
        }
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
