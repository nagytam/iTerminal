using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services;

public class SettingsService : ISettingsService
{
    public const string FOLDER_HISTORY = "FOLDER_HISTORY";

    /// <summary>
    /// Keys under this prefix are not the settings service's to keep. It reads the whole table at
    /// start-up and writes the whole cache back later, so anything it caches it also republishes —
    /// which is exactly how a stale schema stamp once survived every attempt to replace it, and had
    /// the database recreated on every run. Reserved keys are neither read nor written here; the
    /// component that owns one keeps it in its own table.
    /// </summary>
    private const string ReservedKeyPrefix = "__";

    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly Dictionary<string, string> _settings = [];
    private readonly Lock _lock = new();

    public bool IsDirty { get; private set; } = false;

    public SettingsService(IDbContextFactory<iTerminalDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        Read();
    }

    public SettingsService() : this(new TemporaryDbContextFactory())
    {
    }

    public void Set(string key, string value)
    {
        if (IsReserved(key))
        {
            return;
        }

        lock (_lock)
        {
            _settings[key] = value;
            IsDirty = true;
        }
    }

    /// <summary>True for a key owned by another component — see <see cref="ReservedKeyPrefix"/>.</summary>
    private static bool IsReserved(string key) => key.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal);

    public void Delete(string key)
    {
        lock (_lock)
        {
            _settings.Remove(key);
            IsDirty = true;
        }
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out string? value))
            {
                return value;
            }
        }

        return null;
    }

    public bool? GetAsBool(string key, bool? defaultValue = false)
    {
        string? boolValue = Get(key);
        if (bool.TryParse(boolValue, out bool value))
        {
            return value;
        }

        return defaultValue;
    }

    private void Read()
    {
        lock (_lock)
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            _settings.Clear();
            foreach (PersistedSetting persistedSetting in context.Settings.AsNoTracking())
            {
                if (IsReserved(persistedSetting.Key))
                {
                    continue;
                }

                _settings[persistedSetting.Key] = persistedSetting.Value;
            }

            IsDirty = false;
        }
    }

    public void Write()
    {
        lock (_lock)
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            Dictionary<string, PersistedSetting> existingSettings = context.Settings
                .ToDictionary(item => item.Key, StringComparer.Ordinal);

            foreach (KeyValuePair<string, PersistedSetting> existingSetting in existingSettings)
            {
                // Reserved rows are dropped rather than kept: this is what clears a stamp left in
                // this table by an older version, which would otherwise be republished for ever.
                if (!_settings.ContainsKey(existingSetting.Key))
                {
                    context.Settings.Remove(existingSetting.Value);
                }
            }

            foreach (KeyValuePair<string, string> setting in _settings)
            {
                if (existingSettings.TryGetValue(setting.Key, out PersistedSetting? persistedSetting))
                {
                    persistedSetting.Value = setting.Value;
                }
                else
                {
                    context.Settings.Add(new PersistedSetting
                    {
                        Key = setting.Key,
                        Value = setting.Value,
                    });
                }
            }

            context.SaveChanges();
            IsDirty = false;
        }
    }

    private sealed class TemporaryDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public TemporaryDbContextFactory()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.Settings.{Guid.NewGuid():N}.sqlite");
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext()
        {
            return new iTerminalDbContext(_options);
        }

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
