using iTerminal.Core.Application;
using Microsoft.Extensions.Configuration;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// Configuration that points the application's <c>DataDirectory</c> at a fresh temporary folder. A test that
/// builds the real container uses it, so it never opens the user's own database, and two such tests that run
/// at once each get a database of their own instead of creating the same tables in one file.
/// </summary>
internal static class TemporaryDataDirectory
{
    public static IConfiguration Create() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [iTerminalApplicationBuilderExtension.DataDirectoryKey] = Path.Combine(
                Path.GetTempPath(), "iTerminal.DataDirectory." + Guid.NewGuid().ToString("N")),
        })
        .Build();
}
