using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Common;

public class DatabaseClientFactory : IDatabaseClientFactory
{
    public IDatabaseClient CreateClient(ConnectionConfig config)
    {
        return config.ConnectionType switch
        {
            ConnectionType.Sqlite => new SqliteDatabaseClient(config),
            ConnectionType.PostgreSql => new PostgreSqlDatabaseClient(config),
            ConnectionType.MySql => new MySqlDatabaseClient(config),
            ConnectionType.MariaDB => new MariaDbDatabaseClient(config),
            ConnectionType.SqlServer => new SqlServerDatabaseClient(config),
            ConnectionType.CockroachDb => new CockroachDbDatabaseClient(config),
            ConnectionType.Redshift => new RedshiftDatabaseClient(config),
            ConnectionType.LibSql => new LibSqlDatabaseClient(config),
            ConnectionType.DuckDb => new DuckDbDatabaseClient(config),
            ConnectionType.Oracle => new OracleDatabaseClient(config),
            ConnectionType.ClickHouse => new ClickHouseDatabaseClient(config),
            ConnectionType.MongoDb => new MongoDbDatabaseClient(config),
            ConnectionType.Supabase => new SupabaseDatabaseClient(config),
            ConnectionType.Redis => new RedisDatabaseClient(config),
            ConnectionType.PocketBase => new PocketBaseDatabaseClient(config),
            ConnectionType.Appwrite => new AppwriteDatabaseClient(config),
            ConnectionType.Firebase => new FirebaseDatabaseClient(config),
            ConnectionType.AwsAmplify => new AwsAmplifyDatabaseClient(config),
            ConnectionType.Db2 => new Db2DatabaseClient(config),
            ConnectionType.SapHana => new SapHanaDatabaseClient(config),
            ConnectionType.MsAccess => new MsAccessDatabaseClient(config),
            ConnectionType.Firebird => new FirebirdDatabaseClient(config),
            _ => throw new NotSupportedException($"Silnik bazy danych '{config.ConnectionType}' nie jest wspierany.")
        };
    }
}
