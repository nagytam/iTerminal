using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// Reads the names of the tests a <c>dotnet test --list-tests</c> run prints, without running them: the names follow a header
/// line and are indented. Only VSTest (.NET) has a discovery here.
/// </summary>
public static class TestDiscoveryParser
{
    private const string DotNetHeader = "The following Tests are available:";

    /// <summary>The test names in <paramref name="output"/>, in the order the tool printed them. Empty for a language with no discovery.</summary>
    public static IReadOnlyList<string> Parse(DevelopmentLanguage language, IEnumerable<string> output)
    {
        if (language != DevelopmentLanguage.DotNet) return [];

        var names = new List<string>();
        bool listing = false;
        foreach (string line in output)
        {
            if (line.Trim() == DotNetHeader)
            {
                listing = true;
                continue;
            }

            // A name is indented; the summary lines after the list are not.
            if (listing && line.Length > 0 && char.IsWhiteSpace(line[0]) && line.Trim().Length > 0) names.Add(line.Trim());
        }

        return names;
    }
}
