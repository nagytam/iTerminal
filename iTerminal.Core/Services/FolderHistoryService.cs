using iConsole.Data;

namespace iTerminal.Core.Services;

public class FolderHistoryService
{
    private readonly ISettingsService _settingsService;

    public FolderHistoryService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        ReadSettings();
    }

    public int MaxHistoryItems { get; set; } = 20;
    public ConcurrentObservableCollection<string> History { get; private set; } = [];

    /// <summary>
    /// Records a visit. The folder goes to the top of the list; a folder visited again moves up rather than staying where it
    /// was, so the list is in the order the folders were last used.
    /// </summary>
    public void Add(Url url)
    {
        History.Remove(url.FullUrl);
        while (History.Count >= MaxHistoryItems)
        {
            History.RemoveAt(History.Count - 1);
        }

        History.Insert(0, url.FullUrl);
        UpdateSettings();
    }

    private void ReadSettings()
    {
        var history = _settingsService.Get(SettingsService.FOLDER_HISTORY);
        if (history is not null)
        {
            foreach (var folder in FolderListCodec.Split(history).Where(folder => folder.Length > 0))
            {
                History.Add(folder);
            }
        }
    }

    private void UpdateSettings()
    {
        _settingsService.Set(SettingsService.FOLDER_HISTORY, FolderListCodec.Join(History.AsSnapshot()));
    }
}
