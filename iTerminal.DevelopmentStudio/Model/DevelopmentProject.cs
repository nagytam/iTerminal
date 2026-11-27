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

/// <summary>What a .NET project builds to, read from its project file. Only a console or web project has a program to start.</summary>
public enum DotNetOutputKind
{
    /// <summary>The project file does not say (or the scan did not read it). It is offered as startable.</summary>
    Unknown,

    /// <summary>An executable: <c>OutputType</c> <c>Exe</c> or <c>WinExe</c>, or the worker SDK.</summary>
    Console,

    /// <summary>An ASP.NET Core project (the <c>Microsoft.NET.Sdk.Web</c> SDK): it starts a server.</summary>
    Web,

    /// <summary>A class library (the default of <c>Microsoft.NET.Sdk</c> without an <c>OutputType</c>): it has no program to start.</summary>
    Library,
}

/// <summary>
/// One buildable, runnable or testable item in a folder: a solution, a .NET project, or a Python or PowerShell file.
/// </summary>
/// <param name="Name">The name shown in the explorer.</param>
/// <param name="Language">The language the item is built and run with.</param>
/// <param name="Kind">What the item is.</param>
/// <param name="Path">The absolute path of the project, solution or script file.</param>
/// <param name="IsTest">Whether the item holds tests (a test project, or a test file).</param>
/// <param name="SolutionPath">The solution that lists the project, when the scan found one. The explorer draws the project under it.</param>
/// <param name="Output">What a .NET project builds to. Only a console or web project can be the Main program.</param>
public sealed record DevelopmentProject(
    string Name,
    DevelopmentLanguage Language,
    DevelopmentProjectKind Kind,
    string Path,
    bool IsTest = false,
    string? SolutionPath = null,
    DotNetOutputKind Output = DotNetOutputKind.Unknown)
{
    /// <summary>Whether the item has a program the Main program can start: a script, a Java project, or a console or web project. A library and a test project have none.</summary>
    public bool IsStartable => Kind switch
    {
        DevelopmentProjectKind.Solution => false,
        DevelopmentProjectKind.DotNetProject => !IsTest && Output is not DotNetOutputKind.Library,
        _ => true,
    };

    /// <summary>The folder the item lives in. It is the working directory of every command run on the item.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
