using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Common;

/// <summary>Finds the example files in <c>Test/FileTypes</c>, one per supported format, that the format tests read.</summary>
public static class SampleFiles
{
    private static readonly Lazy<string> Folder = new(FindFolder);

    /// <summary>The full path of an example, such as <c>example.ttf</c>.</summary>
    public static string Path(string name) => System.IO.Path.Combine(Folder.Value, name);

    /// <summary>The bytes of an example.</summary>
    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    private static string FindFolder()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "Test", "FileTypes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Inconclusive("The Test/FileTypes folder was not found above the test output folder.");
        return string.Empty;
    }
}
