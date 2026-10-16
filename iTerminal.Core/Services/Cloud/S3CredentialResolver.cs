namespace iTerminal.Core.Services.Cloud;

/// <summary>What an S3 request is signed with and sent to.</summary>
public sealed record S3Credentials(string? AccessKeyId, string? SecretAccessKey, string? SessionToken, string? Region, string? Endpoint);

/// <summary>
/// Finds the credentials for an <c>s3://</c> URL, in this order:
/// <list type="number">
/// <item>An entry in the password manager whose title starts with <c>S3</c> and whose user name is the
/// access key id (the URL's user part; with none in the URL, the first such entry). The password is the
/// secret access key; the notes may add <c>endpoint=</c>, <c>region=</c> and <c>token=</c> lines.</item>
/// <item>The standard environment: <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c>,
/// <c>AWS_SESSION_TOKEN</c>, <c>AWS_REGION</c> / <c>AWS_DEFAULT_REGION</c>, <c>AWS_ENDPOINT_URL_S3</c>.</item>
/// <item>Nothing: unsigned requests, which is how a public bucket is read.</item>
/// </list>
/// Region and endpoint also come from the <c>Cloud:S3</c> configuration when nothing above says.
/// </summary>
public static class S3CredentialResolver
{
    public static S3Credentials Resolve(
        string? accessKeyHint,
        IPasswordManager? passwords,
        S3Options? options,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string? key = null;
        string? secret = null;
        string? token = null;
        string? region = null;
        string? endpoint = null;

        if (passwords is { IsUnlocked: true })
        {
            var entries = passwords.List()
                .Where(e => e.Title.StartsWith("s3", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var entry = string.IsNullOrEmpty(accessKeyHint)
                ? entries.FirstOrDefault()
                : entries.FirstOrDefault(e => string.Equals(e.Username, accessKeyHint, StringComparison.Ordinal));
            if (entry is not null && entry.Password.Length > 0)
            {
                key = entry.Username;
                secret = entry.Password;
                foreach (var line in entry.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var name = line[..separator].Trim().ToLowerInvariant();
                    var value = line[(separator + 1)..].Trim();
                    switch (name)
                    {
                        case "endpoint":
                            endpoint = value;
                            break;
                        case "region":
                            region = value;
                            break;
                        case "token":
                            token = value;
                            break;
                    }
                }
            }
        }

        if (key is null)
        {
            var envKey = environment("AWS_ACCESS_KEY_ID");
            var envSecret = environment("AWS_SECRET_ACCESS_KEY");
            if (!string.IsNullOrEmpty(envKey) && !string.IsNullOrEmpty(envSecret)
                && (string.IsNullOrEmpty(accessKeyHint) || accessKeyHint == envKey))
            {
                key = envKey;
                secret = envSecret;
                token = environment("AWS_SESSION_TOKEN");
            }
        }

        region ??= FirstNonEmpty(options?.Region, environment("AWS_REGION"), environment("AWS_DEFAULT_REGION"));
        endpoint ??= FirstNonEmpty(options?.Endpoint, environment("AWS_ENDPOINT_URL_S3"), environment("AWS_ENDPOINT_URL"));
        return new S3Credentials(key, secret, token, region, endpoint);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
