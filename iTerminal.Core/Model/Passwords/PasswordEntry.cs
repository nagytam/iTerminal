namespace iTerminal.Core.Model.Passwords;

/// <summary>What a saved password is for. It groups the list and tells the person which feature uses it.</summary>
public enum PasswordCategory
{
    Network,
    Mail,
    Service,
    Other,
}

/// <summary>
/// One saved login. <see cref="Id"/> is the key a feature asks for it by (a network profile's id, a mail
/// account's <c>mail:</c> id), so the feature and the manager never need to agree on anything else.
/// </summary>
public sealed record PasswordEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public PasswordCategory Category { get; init; } = PasswordCategory.Other;
    public string Password { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset Updated { get; init; }
}
