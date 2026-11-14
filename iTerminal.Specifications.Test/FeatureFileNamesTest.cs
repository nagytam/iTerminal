using System.Runtime.CompilerServices;

namespace iTerminal.Specifications.Test;

/// <summary>
/// Keeps the feature files in their place in the feature hierarchy: each one sits in the folder of its area,
/// is named <c>Area.Feature.feature</c>, and holds at least one scenario. The areas are listed here so that a
/// new area is added on purpose. Reads files only; it never touches the machine.
/// </summary>
[TestClass]
public sealed class FeatureFileNamesTest
{
    private static readonly string[] Areas =
    [
        "Application", "Archives", "Compare", "Developer", "Documents", "Drives", "Editors", "Files", "Music",
        "Network", "Panels", "Search", "Security", "Utilities", "Viewers",
    ];

    [TestMethod]
    public void EveryFeatureFileSitsInItsAreaFolderAndIsNamedAfterIt()
    {
        string features = Path.Combine(ProjectDirectory(), "Features");
        string[] files = Directory.GetFiles(features, "*.feature", SearchOption.AllDirectories);

        Assert.IsTrue(files.Length > 0, "no feature files were found");
        foreach (string file in files)
        {
            string area = Path.GetFileName(Path.GetDirectoryName(file)!);
            string name = Path.GetFileNameWithoutExtension(file);
            Assert.IsTrue(Areas.Contains(area), $"{file} is in an unknown area folder");
            Assert.IsTrue(name.StartsWith(area + ".", StringComparison.Ordinal) && name.Length > area.Length + 1,
                $"{Path.GetFileName(file)} is not named {area}.<Feature>.feature");
            Assert.IsTrue(name.Substring(area.Length + 1).All(char.IsLetterOrDigit),
                $"{Path.GetFileName(file)} has a name part that is not a single word");
        }
    }

    [TestMethod]
    public void EveryFeatureFileHoldsAtLeastOneScenario()
    {
        string features = Path.Combine(ProjectDirectory(), "Features");
        foreach (string file in Directory.GetFiles(features, "*.feature", SearchOption.AllDirectories))
        {
            bool hasScenario = File.ReadLines(file).Any(line => line.TrimStart().StartsWith("Scenario", StringComparison.Ordinal));
            Assert.IsTrue(hasScenario, $"{Path.GetFileName(file)} has no scenario");
        }
    }

    private static string ProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
