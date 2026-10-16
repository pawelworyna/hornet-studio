using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class AwsAmplifyDatabaseClient : PostgreSqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.AwsAmplify;

    public AwsAmplifyDatabaseClient(ConnectionConfig config) : base(config)
    {
        if (string.IsNullOrWhiteSpace(config.DefaultDatabase))
        {
            config.DefaultDatabase = "amplify";
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var raw = await base.GetVersionAsync(ct);
        return $"AWS Amplify Aurora/PostgreSQL ({raw})";
    }
}
