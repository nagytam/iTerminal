namespace iTerminal.Core.Services.Shell;

/// <summary>
/// The environment a command-log row shows on its Environment tab: the app's own variables, one per line, as
/// the one-shot path has always recorded them. A session's own exports are not listed.
/// </summary>
public static class CommandEnvironment
{
    public static string Describe()
    {
        var text = new System.Text.StringBuilder();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            text.Append(entry.Key).Append('=').Append(entry.Value).Append(Environment.NewLine);
        }

        return text.ToString();
    }
}
