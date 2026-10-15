using System.IO;
using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

public sealed class NullContentModelService : IContentModelService
{
    public static readonly NullContentModelService Instance = new();

    private NullContentModelService()
    {
    }

    public ConcurrentList<IContentBrowser> ContentBrowsers { get; } = [];
    public ConcurrentList<IContentModel> ContentModels { get; } = [];
    public Url? FocusedFileUrl => null;
    public string FocusedPanel { get; set; } = string.Empty;
    public string LastFocusedPanel { get; set; } = string.Empty;
    public Url? NonFocusedFolderUrl => null;
    public Url Url => new(Directory.GetCurrentDirectory());

    public event EventHandler? ModelChanged
    {
        add { }
        remove { }
    }

    public void AddContentBrowser(string name, IContentBrowser contentBrowser)
    {
    }

    public IContentModel? AddContentModel(string name, Url url)
    {
        return null;
    }

    public IContentModel? GetFocusedModel()
    {
        return null;
    }

    public IContentModel? GetLastContentModel()
    {
        return null;
    }

    public Task SetUrlAsync(Url selectedDrivePath)
    {
        return Task.CompletedTask;
    }

    public Task SetUrlAsync(string name, Url selectedDrivePath)
    {
        return Task.CompletedTask;
    }
}
