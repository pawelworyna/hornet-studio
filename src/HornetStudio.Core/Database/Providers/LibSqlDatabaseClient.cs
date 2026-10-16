using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class LibSqlDatabaseClient : SqliteDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.LibSql;

    public LibSqlDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var ver = await base.GetVersionAsync(ct);
        return $"LibSQL / Turso (SQLite {ver})";
    }
}
