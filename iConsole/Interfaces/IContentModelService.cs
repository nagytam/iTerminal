using iConsole.Data;

namespace iConsole.Interfaces;

public interface IContentModelService
{
    ConcurrentList<IContentBrowser> ContentBrowsers { get; }
    ConcurrentList<IContentModel> ContentModels { get; }
    Url? FocusedFileUrl { get; }
    string FocusedPanel { get; set; }
    string LastFocusedPanel { get; set; }
    Url? NonFocusedFolderUrl { get; }
    Url Url { get; }
    event EventHandler ModelChanged;

    void AddContentBrowser(string name, IContentBrowser contentBrowser);
    IContentModel? AddContentModel(string name, Url url);
    IContentModel? GetFocusedModel();
    IContentModel? GetLastContentModel();
    Task SetUrlAsync(Url selectedDrivePath);
    Task SetUrlAsync(string name, Url selectedDrivePath);
}