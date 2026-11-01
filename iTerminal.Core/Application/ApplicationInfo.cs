using System.Reflection;

namespace iTerminal.Core.Application;

/// <summary>The name and version the app shows: "iTerminal BETA 0.0.0.42".</summary>
public static class ApplicationInfo
{
    public static string Version { get; } = ReadVersion();

    public const string Name = "iTerminal BETA";

    public static string Title => $"{Name} {Version}";

    private static string ReadVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationInfo).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit hash>" to the informational version.
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
