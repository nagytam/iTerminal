namespace iTerminal.DevelopmentStudio.Model;

/// <summary>
/// The build configuration of the .NET commands (<c>dotnet build -c</c>). Its name is the MSBuild name, so it is passed as it is.
/// Release disables debugging: the program is built optimised and the debugger does not start it.
/// </summary>
public enum BuildConfiguration
{
    Debug,
    Release,
}
