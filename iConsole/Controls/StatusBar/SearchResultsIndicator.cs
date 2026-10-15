using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class SearchResultsIndicator : StatusBarItem
{
    private ISearchModel _searchModel { get; set; }
    public SearchResultsIndicator(
        ISearchModel searchModel,
        IThemeService themeService)
        : base(themeService)
    {
        _searchModel = searchModel;
        _searchModel.PropertyChanged += OnPropertyChanged;
        Name = nameof(SearchResultsIndicator);
        ViewSize = new Size(10, 1);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var s = new ConxelString($"Search: {_searchModel.Hits.Count}", Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
