using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class RedshiftDatabaseClient : PostgreSqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.Redshift;

    public RedshiftDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var ver = await base.GetVersionAsync(ct);
        return $"Amazon Redshift ({ver})";
    }
}
