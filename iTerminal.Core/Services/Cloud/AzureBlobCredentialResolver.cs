using iConsole.Services;

namespace iTerminal.Core.Services.Cloud;

/// <summary>What a request to one storage account is signed with, and where it is sent.</summary>
/// <param name="Account">The storage account's name.</param>
/// <param name="Key">The account key (base64), or null for anonymous requests, which is how a public container is read.</param>
/// <param name="Endpoint">A blob endpoint other than the public one (Azurite, a sovereign cloud); null means <c>https://account.blob.core.windows.net</c>.</param>
public sealed record AzureBlobCredentials(string Account, string? Key, string? Endpoint);

/// <summary>The <c>Cloud:AzureBlob</c> configuration.</summary>
public class AzureBlobOptions
{
    /// <summary>A blob endpoint used for every account (path-style, as the Azure storage emulator needs). Empty means the public one.</summary>
    public string? Endpoint { get; set; }
}

/// <summary>
/// Finds the credentials of an <c>azblob://</c> account, in this order:
/// <list type="number">
/// <item>An entry in the password manager whose title starts with <c>Azure</c> and whose user name is the
/// storage account's name. The password is the account key; the notes may add an <c>endpoint=</c> line.</item>
/// <item>The standard environment: <c>AZURE_STORAGE_ACCOUNT</c> and <c>AZURE_STORAGE_KEY</c>, when the
/// account is that one.</item>
/// <item>Nothing: anonymous requests, which is how a public container is read.</item>
/// </list>
/// The endpoint also comes from the <c>Cloud:AzureBlob</c> configuration when no entry names one.
/// </summary>
public static class AzureBlobCredentialResolver
{
    /// <summary>The storage accounts the password manager holds, for the root of <c>azblob://</c>.</summary>
    public static IReadOnlyList<string> SavedAccounts(IPasswordManager? passwords)
    {
        if (passwords is not { IsUnlocked: true })
        {
            return [];
        }

        return [.. passwords.List()
            .Where(e => e.Title.StartsWith("Azure", StringComparison.OrdinalIgnoreCase) && e.Username.Length > 0)
            .Select(e => e.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)];
    }

    public static AzureBlobCredentials Resolve(
        string account,
        IPasswordManager? passwords,
        AzureBlobOptions? options,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string? key = null;
        string? endpoint = null;

        if (passwords is { IsUnlocked: true })
        {
            var entry = passwords.List().FirstOrDefault(e =>
                e.Title.StartsWith("Azure", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Username, account, StringComparison.OrdinalIgnoreCase)
                && e.Password.Length > 0);
            if (entry is not null)
            {
                key = entry.Password;
                foreach (var line in entry.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var separator = line.IndexOf('=');
                    if (separator > 0 && line[..separator].Trim().Equals("endpoint", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoint = line[(separator + 1)..].Trim();
                    }
                }
            }
        }

        if (key is null
            && string.Equals(environment("AZURE_STORAGE_ACCOUNT"), account, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(environment("AZURE_STORAGE_KEY")))
        {
            key = environment("AZURE_STORAGE_KEY");
        }

        endpoint ??= string.IsNullOrWhiteSpace(options?.Endpoint) ? null : options.Endpoint;
        return new AzureBlobCredentials(account, key, endpoint);
    }
}
