using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model.Mail;

/// <summary>
/// One mail account: where its POP3 inbox, SMTP outgoing server and LDAP address book are, and who it is.
/// The password is not part of the record — it lives in the vault under <see cref="SecretProfileId"/>, so
/// the settings value never holds a secret.
/// </summary>
public sealed record MailAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string PopHost { get; init; } = string.Empty;
    public int PopPort { get; init; } = 995;
    public bool PopTls { get; init; } = true;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public bool SmtpTls { get; init; } = true;
    public string UserName { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string LdapHost { get; init; } = string.Empty;
    public int LdapPort { get; init; } = 636;
    public bool LdapTls { get; init; } = true;
    public string LdapBaseDn { get; init; } = string.Empty;
    public string LdapBindDn { get; init; } = string.Empty;

    public string SecretProfileId => "mail:" + Id;
}

/// <summary>One line of the inbox: what the list shows, read from the message's headers.</summary>
public sealed record MailListing(int Number, long Size, string From, string Subject, string Date);

/// <summary>A whole message: its headers and its text body.</summary>
public sealed record MailItem(int Number, string From, string To, string Subject, string Date, string Body);

/// <summary>A person found in the address book.</summary>
public sealed record DirectoryPerson(string Name, string Mail, string Phone)
{
    /// <summary>"Name &lt;mail&gt;", the form a recipient is written in.</summary>
    public string AsRecipient => string.IsNullOrEmpty(Name) ? Mail : $"{Name} <{Mail}>";
}

/// <summary>
/// The configured accounts, kept as one JSON list in the settings table under <see cref="SettingsKey"/>. Not
/// a database table, for the reason the calendar gives: a new table would change the schema stamp.
/// </summary>
public sealed class MailAccountStore(ISettingsService settingsService)
{
    public const string SettingsKey = "MailAccounts";

    private static readonly JsonSerializerOptions Json = new();

    private List<MailAccount>? _accounts;

    /// <summary>The configured accounts. The dialog works with the first.</summary>
    public IReadOnlyList<MailAccount> Accounts
    {
        get
        {
            EnsureLoaded();
            return _accounts!.AsReadOnly();
        }
    }

    public void Save(MailAccount account)
    {
        EnsureLoaded();
        var index = _accounts!.FindIndex(a => a.Id == account.Id);
        if (index < 0)
        {
            _accounts.Add(account);
        }
        else
        {
            _accounts[index] = account;
        }

        Write();
    }

    public void Remove(string id)
    {
        EnsureLoaded();
        if (_accounts!.RemoveAll(a => a.Id == id) > 0)
        {
            Write();
        }
    }

    private void EnsureLoaded()
    {
        if (_accounts is not null)
        {
            return;
        }

        _accounts = [];
        try
        {
            var json = settingsService.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _accounts.AddRange(JsonSerializer.Deserialize<List<MailAccount>>(json, Json) ?? []);
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the mail dialog opening; the next save replaces it.
        }
    }

    private void Write()
    {
        settingsService.Set(SettingsKey, JsonSerializer.Serialize(_accounts, Json));
        settingsService.Write();
    }
}
