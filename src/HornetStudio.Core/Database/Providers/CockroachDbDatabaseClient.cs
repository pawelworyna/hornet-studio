using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class CockroachDbDatabaseClient : PostgreSqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.CockroachDb;

    public CockroachDbDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var ver = await base.GetVersionAsync(ct);
        return ver.Contains("CockroachDB", StringComparison.OrdinalIgnoreCase) ? ver : $"CockroachDB ({ver})";
    }
}
