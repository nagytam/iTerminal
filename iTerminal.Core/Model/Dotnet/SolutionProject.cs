namespace iTerminal.Core.Model.Dotnet;

/// <summary>
/// A single project referenced by a solution file.
/// </summary>
/// <param name="Name">Project name (file name without extension).</param>
/// <param name="RelativePath">Project path as written in the solution file, relative to it.</param>
/// <param name="AbsolutePath">Absolute path to the project file on disk.</param>
/// <param name="IsTestProject">Whether the project file declares itself a test project (<c>IsTestProject</c> MSBuild property, or a <c>Microsoft.NET.Test.Sdk</c> package reference as a fallback).</param>
public sealed record SolutionProject(string Name, string RelativePath, string AbsolutePath, bool IsTestProject = false);
