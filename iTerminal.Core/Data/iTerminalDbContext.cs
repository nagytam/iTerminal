using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace iTerminal.Core.Data;

public class iTerminalDbContext(DbContextOptions<iTerminalDbContext> options) : DbContext(options)
{
    public DbSet<PersistedSetting> Settings => Set<PersistedSetting>();

    public DbSet<DatabaseMetadataEntry> DatabaseMetadata => Set<DatabaseMetadataEntry>();

    public DbSet<FolderIndexCacheEntry> FolderIndexCacheEntries => Set<FolderIndexCacheEntry>();
    public DbSet<FolderContentsCacheEntry> FolderContentsCacheEntries => Set<FolderContentsCacheEntry>();

    public DbSet<CachedWeatherInfo> CachedWeatherInfos => Set<CachedWeatherInfo>();
    public DbSet<CachedInstrumentValue> CachedInstrumentValues => Set<CachedInstrumentValue>();

    public DbSet<VaultEntry> VaultEntries => Set<VaultEntry>();

    public DbSet<FileLabelEntry> FileLabels => Set<FileLabelEntry>();

    public DbSet<Music.MusicLibraryEntry> MusicLibraryEntries => Set<Music.MusicLibraryEntry>();
    public DbSet<Music.MusicPlaylistEntry> MusicPlaylistEntries => Set<Music.MusicPlaylistEntry>();

    public DbSet<Documents.DocumentEntry> DocumentEntries => Set<Documents.DocumentEntry>();
    public DbSet<Documents.DocumentContentEntry> DocumentContentEntries => Set<Documents.DocumentContentEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersistedSetting>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Key).HasMaxLength(256);
            entity.Property(item => item.Value).HasMaxLength(4000);
        });

        modelBuilder.Entity<DatabaseMetadataEntry>(entity =>
        {
            entity.ToTable("DatabaseMetadata");
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Key).HasMaxLength(128);
            entity.Property(item => item.Value).HasMaxLength(512);
        });

        modelBuilder.Entity<FolderIndexCacheEntry>(entity =>
        {
            entity.ToTable("FolderIndexCache");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);
        });

        modelBuilder.Entity<FolderContentsCacheEntry>(entity =>
        {
            entity.ToTable("FolderContentsCache");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);
            entity.Property(item => item.ContentJson).HasColumnType("TEXT");

            // Store DateTimeOffset as Unix milliseconds (INTEGER) so comparisons translate to SQL
            var dtoToLong = new ValueConverter<DateTimeOffset, long>(
                v => v.ToUnixTimeMilliseconds(),
                v => DateTimeOffset.FromUnixTimeMilliseconds(v));

            entity.Property(e => e.SavedAtUtc)
                .HasConversion(dtoToLong)
                .HasColumnType("INTEGER");
        });

        modelBuilder.Entity<CachedWeatherInfo>(entity =>
        {
            entity.ToTable("CachedWeatherInfo");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.LocationName).HasMaxLength(256);
        });

        modelBuilder.Entity<CachedInstrumentValue>(entity =>
        {
            entity.ToTable("CachedInstrumentValues");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.InstrumentName).HasMaxLength(256);
            entity.Property(item => item.Currency).HasMaxLength(50);
        });

        modelBuilder.Entity<FileLabelEntry>(entity =>
        {
            entity.ToTable("FileLabels");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);
            entity.Property(item => item.Colour).HasMaxLength(16);
            entity.Property(item => item.Tags).HasMaxLength(1000);
        });

        modelBuilder.Entity<VaultEntry>(entity =>
        {
            entity.ToTable("VaultEntries");
            entity.HasKey(item => item.ProfileId);
            entity.Property(item => item.ProfileId).HasMaxLength(256);
            entity.Property(item => item.EncryptedSecret).HasMaxLength(4000);
        });

        modelBuilder.Entity<Music.MusicLibraryEntry>(entity =>
        {
            entity.ToTable("MusicLibrary");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);
            entity.Property(item => item.Title).HasMaxLength(512);
            entity.Property(item => item.Artist).HasMaxLength(512);
            entity.Property(item => item.Album).HasMaxLength(512);

            // Searching by artist and album is the whole point of the index, and both are looked up
            // far more often than the table is written.
            entity.HasIndex(item => item.Artist);
            entity.HasIndex(item => item.Album);
        });

        modelBuilder.Entity<Music.MusicPlaylistEntry>(entity =>
        {
            entity.ToTable("MusicPlaylist");

            // The position is the key, which is what makes the queue an ordered list rather than a
            // set of tracks that happen to be saved. Never generated: it is assigned by the writer.
            entity.HasKey(item => item.Position);
            entity.Property(item => item.Position).ValueGeneratedNever();
            entity.Property(item => item.Path).HasMaxLength(4096);
            entity.Property(item => item.Title).HasMaxLength(512);
            entity.Property(item => item.Artist).HasMaxLength(512);
            entity.Property(item => item.Album).HasMaxLength(512);
        });

        modelBuilder.Entity<Documents.DocumentEntry>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);
            entity.Property(item => item.FileName).HasMaxLength(512);
            entity.Property(item => item.RelativeFolder).HasMaxLength(4096);
            entity.Property(item => item.Extension).HasMaxLength(32);
            entity.Property(item => item.Title).HasMaxLength(512);
            entity.Property(item => item.Author).HasMaxLength(512);
            entity.Property(item => item.ExtractionNote).HasMaxLength(512);

            // The results list orders by modification date and filters by kind, and both are read
            // far more often than the table is written.
            entity.HasIndex(item => item.ModifiedUtc);
            entity.HasIndex(item => item.Kind);
        });

        modelBuilder.Entity<Documents.DocumentContentEntry>(entity =>
        {
            entity.ToTable("DocumentContents");
            entity.HasKey(item => item.Path);
            entity.Property(item => item.Path).HasMaxLength(4096);

            // No length cap: the extractors already apply DocumentTextLimits.MaxTextLength, and a
            // second, smaller limit here would silently truncate what they deliberately kept.
            entity.Property(item => item.Text).HasColumnType("TEXT");
        });
    }
}
