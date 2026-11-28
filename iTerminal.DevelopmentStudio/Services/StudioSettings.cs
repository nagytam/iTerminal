using System.Text.Json;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// The keys the Development studio keeps in the settings table, and the list encoding of the ones that hold a list. The dialog
/// reads them when it is created and writes each one when the user changes it: a write only changes the settings cache in
/// memory, and the settings synchronisation job saves it in the background, so a key handler never waits on the database.
/// The keys are not reserved (they do not start with <c>__</c>).
/// </summary>
public static class StudioSettings
{
    /// <summary>The panel arrangement, as <see cref="Model.StudioPanelLayout.Serialize"/> writes it.</summary>
    public const string LayoutKey = "DevelopmentStudio.Layout";

    /// <summary>The SBOM format: <c>Xml</c> or <c>Json</c>.</summary>
    public const string SbomFormatKey = "DevelopmentStudio.SbomFormat";

    /// <summary>The build configuration of the .NET commands: <c>Debug</c> or <c>Release</c>. Release disables debugging.</summary>
    public const string BuildConfigurationKey = "DevelopmentStudio.BuildConfiguration";

    /// <summary>The host path of the .NET SDK new commands run with.</summary>
    public const string SdkHostKey = "DevelopmentStudio.SdkHost";

    /// <summary>The left pane: <c>Solution</c>, <c>Tests</c> or <c>Files</c>.</summary>
    public const string ExplorerModeKey = "DevelopmentStudio.ExplorerMode";

    /// <summary>The tab of the output panel: <c>Output</c>, <c>Errors</c>, <c>Debug</c>, <c>Coverage</c> or <c>Profile</c>.</summary>
    public const string OutputTabKey = "DevelopmentStudio.OutputTab";

    /// <summary>The seconds the last code analysis of each item took, as a JSON object (path to seconds): the estimate of the next one.</summary>
    public const string AnalysisDurationsKey = "DevelopmentStudio.AnalysisDurations";

    /// <summary>The paths of the expanded solutions, projects and folders of the Solution Explorer, as a JSON list.</summary>
    public const string ExpandedFoldersKey = "DevelopmentStudio.ExpandedFolders";

    /// <summary>The path of the row the Solution Explorer's cursor is on. Empty or absent when none was saved.</summary>
    public const string SelectedPathKey = "DevelopmentStudio.SelectedPath";

    /// <summary>The durations as a JSON object of seconds keyed by item path.</summary>
    public static string EncodeDurations(IReadOnlyDictionary<string, double> durations) => JsonSerializer.Serialize(durations);

    /// <summary>The durations a stored value holds; a missing or damaged value gives none, as <see cref="DecodeList"/> does.</summary>
    public static IReadOnlyDictionary<string, double> DecodeDurations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, double>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(text) ?? new Dictionary<string, double>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, double>();
        }
    }

    /// <summary>A list as a JSON array of strings.</summary>
    public static string EncodeList(IEnumerable<string> items) => JsonSerializer.Serialize(items);

    /// <summary>
    /// The list a stored value holds. A missing value gives an empty list, and so does a damaged one: a list that cannot be read
    /// is replaced by the next save rather than blocking the studio.
    /// </summary>
    public static IReadOnlyList<string> DecodeList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(text)?.Where(item => !string.IsNullOrEmpty(item)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
