using Microsoft.EntityFrameworkCore;
using HornetStudio.Core.Storage.Entities;

namespace HornetStudio.Core.Storage;

public class HornetDbContext : DbContext
{
    public DbSet<SavedConnectionEntity> SavedConnections => Set<SavedConnectionEntity>();
    public DbSet<ConnectionFolderEntity> ConnectionFolders => Set<ConnectionFolderEntity>();
    public DbSet<FavoriteQueryEntity> FavoriteQueries => Set<FavoriteQueryEntity>();
    public DbSet<QueryFolderEntity> QueryFolders => Set<QueryFolderEntity>();
    public DbSet<QueryHistoryEntity> QueryHistory => Set<QueryHistoryEntity>();
    public DbSet<OpenTabEntity> OpenTabs => Set<OpenTabEntity>();
    public DbSet<UserSettingEntity> UserSettings => Set<UserSettingEntity>();

    public HornetDbContext(DbContextOptions<HornetDbContext> options) : base(options)
    {
    }

    public static HornetDbContext CreateDefault(string? customDbPath = null)
    {
        var dbPath = customDbPath;
        if (string.IsNullOrEmpty(dbPath))
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HornetStudio");
            Directory.CreateDirectory(folder);
            dbPath = Path.Combine(folder, "hornet_studio.db");
        }

        var optionsBuilder = new DbContextOptionsBuilder<HornetDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        var context = new HornetDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();
        return context;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Relacje folderów połączeń
        modelBuilder.Entity<SavedConnectionEntity>()
            .HasOne(c => c.Folder)
            .WithMany(f => f.Connections)
            .HasForeignKey(c => c.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Relacje folderów zapytań
        modelBuilder.Entity<FavoriteQueryEntity>()
            .HasOne(q => q.Folder)
            .WithMany(f => f.Queries)
            .HasForeignKey(q => q.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indeksy wyszukiwania
        modelBuilder.Entity<SavedConnectionEntity>()
            .HasIndex(c => c.Name);

        modelBuilder.Entity<FavoriteQueryEntity>()
            .HasIndex(q => q.Title);

        modelBuilder.Entity<QueryHistoryEntity>()
            .HasIndex(h => h.ExecutedAt);
    }
}
