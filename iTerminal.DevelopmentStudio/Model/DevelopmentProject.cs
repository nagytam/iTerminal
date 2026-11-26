namespace iTerminal.DevelopmentStudio.Model;

/// <summary>What a row of the Solution Explorer stands for.</summary>
public enum DevelopmentProjectKind
{
    /// <summary>A .NET solution (<c>.sln</c> or <c>.slnx</c>). Builds, cleans and tests its projects; not run itself.</summary>
    Solution,

    /// <summary>A .NET project file (<c>.csproj</c>, <c>.fsproj</c>, <c>.vbproj</c>).</summary>
    DotNetProject,

    /// <summary>A Python, PowerShell or Java source file (or a test file of one of them).</summary>
    Script,

    /// <summary>A Maven (<c>pom.xml</c>) or Gradle (<c>build.gradle</c>, <c>build.gradle.kts</c>) Java project.</summary>
    JavaProject,
}

/// <summary>
/// One buildable, runnable or testable item in a folder: a solution, a .NET project, or a Python or PowerShell file.
/// </summary>
/// <param name="Name">The name shown in the explorer.</param>
/// <param name="Language">The language the item is built and run with.</param>
/// <param name="Kind">What the item is.</param>
/// <param name="Path">The absolute path of the project, solution or script file.</param>
/// <param name="IsTest">Whether the item holds tests (a test project, or a test file).</param>
public sealed record DevelopmentProject(
    string Name,
    DevelopmentLanguage Language,
    DevelopmentProjectKind Kind,
    string Path,
    bool IsTest = false)
{
    /// <summary>The folder the item lives in. It is the working directory of every command run on the item.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
