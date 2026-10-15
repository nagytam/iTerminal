using iConsole.Data;
using iConsole.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model;

/// <summary>
/// Completions for the folder selector's Path box: the paths typed there before, plus every folder
/// the panels have actually visited.
///
/// The two sources are kept apart rather than merged into one persisted list. The history belongs to
/// <see cref="FolderHistoryService"/> and changes while the dialog is open; copying it into the
/// settings key would both duplicate it on disk and freeze it at whatever it was the first time the
/// Path page was opened. Only what the user types here is persisted.
/// </summary>
public class FolderPathCompletionModel : ICompletionModel
{
    public const string SettingsKey = "FolderSelector-Paths";

    private readonly PersistedValuesCompletionModel _typedPaths;
    private readonly FolderHistoryService? _folderHistoryService;

    public FolderPathCompletionModel(ISettingsService settingsService, FolderHistoryService? folderHistoryService)
    {
        _typedPaths = new PersistedValuesCompletionModel(settingsService, SettingsKey);
        _folderHistoryService = folderHistoryService;
    }

    public ConcurrentObservableCollection<string> MatchingValues { get; } = [];

    public Task? UpdateTask { get; set; }

    public IEnumerable<string> Completions
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var typedPath in _typedPaths.Completions)
            {
                if (!string.IsNullOrWhiteSpace(typedPath) && seen.Add(typedPath))
                {
                    yield return typedPath;
                }
            }
            foreach (var visitedFolder in _folderHistoryService?.History.AsSnapshot() ?? [])
            {
                if (!string.IsNullOrWhiteSpace(visitedFolder) && seen.Add(visitedFolder))
                {
                    yield return visitedFolder;
                }
            }
        }
    }

    public void InsertCompletion(string value) => _typedPaths.InsertCompletion(value);

    public void UpdateMatchingValues(string value)
    {
        MatchingValues.Clear();
        foreach (var completion in Completions.Where(c => c.Contains(value, StringComparison.InvariantCultureIgnoreCase)))
        {
            MatchingValues.Add(completion);
        }
    }
}
