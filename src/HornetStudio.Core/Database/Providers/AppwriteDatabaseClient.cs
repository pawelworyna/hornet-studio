using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class AppwriteDatabaseClient : MySqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.Appwrite;

    public AppwriteDatabaseClient(ConnectionConfig config) : base(config)
    {
        if (string.IsNullOrWhiteSpace(config.DefaultDatabase))
        {
            config.DefaultDatabase = "appwrite";
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var raw = await base.GetVersionAsync(ct);
        return $"Appwrite MariaDB/MySQL ({raw})";
    }
}
