using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class MariaDbDatabaseClient : MySqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.MariaDB;

    public MariaDbDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var ver = await base.GetVersionAsync(ct);
        return ver.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) ? ver : $"MariaDB ({ver})";
    }
}
