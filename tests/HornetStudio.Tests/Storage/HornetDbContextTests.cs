using FluentAssertions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HornetStudio.Tests.Storage;

public class HornetDbContextTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly HornetDbContext _db;

    public HornetDbContextTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"hornet_test_{Guid.NewGuid()}.db");
        var opts = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite($"Data Source={_tempDbFile}")
            .Options;
        _db = new HornetDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
    }

    [Fact]
    public async Task CanSaveAndRetrieve_ConnectionWithFolder()
    {
        var folder = new ConnectionFolderEntity
        {
            Name = "Production Databases",
            Color = "#10B981"
        };
        _db.ConnectionFolders.Add(folder);
        await _db.SaveChangesAsync();

        var connection = new SavedConnectionEntity
        {
            Name = "Prod Postgres",
            ConnectionType = ConnectionType.PostgreSql,
            Host = "db.hornet.internal",
            Port = 5432,
            DefaultDatabase = "prod_db",
            Username = "postgres",
            FolderId = folder.Id,
            IsPinned = true
        };
        _db.SavedConnections.Add(connection);
        await _db.SaveChangesAsync();

        var loaded = await _db.SavedConnections
            .Include(c => c.Folder)
            .FirstOrDefaultAsync(c => c.Id == connection.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Prod Postgres");
        loaded.Folder.Should().NotBeNull();
        loaded.Folder!.Name.Should().Be("Production Databases");
        loaded.IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task CanRecord_QueryHistory()
    {
        var history = new QueryHistoryEntity
        {
            QueryText = "SELECT count(*) FROM users",
            ConnectionName = "Prod Postgres",
            DatabaseName = "prod_db",
            DurationMs = 45,
            AffectedRows = 1,
            Status = "Completed"
        };

        _db.QueryHistory.Add(history);
        await _db.SaveChangesAsync();

        var items = await _db.QueryHistory.ToListAsync();
        items.Should().HaveCount(1);
        items[0].QueryText.Should().Be("SELECT count(*) FROM users");
    }
}
