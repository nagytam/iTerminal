namespace iTerminal.Core.Data;

/// <summary>
/// A fact about the database file itself, written and read only by
/// <see cref="Application.DatabaseInitializationHostedService"/> — currently the hash of the EF model
/// the schema was created from.
///
/// <para>It has its own table rather than living in <c>Settings</c> because that table has an owner:
/// <see cref="Services.SettingsService"/> reads it whole at start-up and writes it whole back later.
/// A schema stamp kept there is read into that cache before the initializer can act on it and written
/// back afterwards, which resurrects the stale value and makes the check fail on every single run —
/// the database was then recreated at every start-up, and everything cached in it (the music library
/// index, the playing queue, the folder cache) was silently thrown away each time.</para>
///
/// <para>It stays inside the database rather than in a file beside it so the stamp cannot drift from
/// the schema it describes: copy or move the file and its stamp goes with it.</para>
/// </summary>
public class DatabaseMetadataEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
