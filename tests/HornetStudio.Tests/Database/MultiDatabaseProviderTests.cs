using FluentAssertions;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Tests.Database;

public class MultiDatabaseProviderTests
{
    private readonly DatabaseClientFactory _factory = new();

    [Theory]
    [InlineData(ConnectionType.Sqlite, typeof(SqliteDatabaseClient))]
    [InlineData(ConnectionType.PostgreSql, typeof(PostgreSqlDatabaseClient))]
    [InlineData(ConnectionType.MySql, typeof(MySqlDatabaseClient))]
    [InlineData(ConnectionType.MariaDB, typeof(MariaDbDatabaseClient))]
    [InlineData(ConnectionType.SqlServer, typeof(SqlServerDatabaseClient))]
    [InlineData(ConnectionType.CockroachDb, typeof(CockroachDbDatabaseClient))]
    [InlineData(ConnectionType.Redshift, typeof(RedshiftDatabaseClient))]
    [InlineData(ConnectionType.LibSql, typeof(LibSqlDatabaseClient))]
    [InlineData(ConnectionType.DuckDb, typeof(DuckDbDatabaseClient))]
    [InlineData(ConnectionType.Oracle, typeof(OracleDatabaseClient))]
    [InlineData(ConnectionType.ClickHouse, typeof(ClickHouseDatabaseClient))]
    [InlineData(ConnectionType.MongoDb, typeof(MongoDbDatabaseClient))]
    [InlineData(ConnectionType.Supabase, typeof(SupabaseDatabaseClient))]
    [InlineData(ConnectionType.Db2, typeof(Db2DatabaseClient))]
    [InlineData(ConnectionType.SapHana, typeof(SapHanaDatabaseClient))]
    [InlineData(ConnectionType.MsAccess, typeof(MsAccessDatabaseClient))]
    public void Factory_CreatesCorrectClientForEveryEngine(ConnectionType type, Type expectedType)
    {
        var config = new ConnectionConfig
        {
            Name = $"Test {type}",
            ConnectionType = type,
            Host = "localhost",
            DatabasePath = type is ConnectionType.Sqlite or ConnectionType.DuckDb or ConnectionType.LibSql or ConnectionType.MsAccess ? ":memory:" : null
        };

        var client = _factory.CreateClient(config);

        client.Should().NotBeNull();
        client.Should().BeOfType(expectedType);
        client.ConnectionType.Should().Be(type);
    }

    [Theory]
    [InlineData(ConnectionType.PostgreSql, 5432)]
    [InlineData(ConnectionType.Supabase, 5432)]
    [InlineData(ConnectionType.MySql, 3306)]
    [InlineData(ConnectionType.MariaDB, 3306)]
    [InlineData(ConnectionType.SqlServer, 1433)]
    [InlineData(ConnectionType.CockroachDb, 26257)]
    [InlineData(ConnectionType.Redshift, 5439)]
    [InlineData(ConnectionType.Oracle, 1521)]
    [InlineData(ConnectionType.ClickHouse, 8123)]
    [InlineData(ConnectionType.MongoDb, 27017)]
    [InlineData(ConnectionType.Db2, 50000)]
    [InlineData(ConnectionType.SapHana, 30015)]
    [InlineData(ConnectionType.MsAccess, 0)]
    public void ConnectionConfig_ResolvesDefaultPortsCorrectly(ConnectionType type, int expectedPort)
    {
        var config = new ConnectionConfig
        {
            ConnectionType = type,
            Port = null
        };

        config.GetEffectivePort().Should().Be(expectedPort);
    }

    [Fact]
    public async Task DuckDb_InMemoryIntegration_WorksEndToEnd()
    {
        var config = new ConnectionConfig
        {
            Name = "DuckDB In-Memory Test",
            ConnectionType = ConnectionType.DuckDb,
            DatabasePath = ":memory:"
        };

        var client = new DuckDbDatabaseClient(config);
        await using (client)
        {
            await client.ConnectAsync();
            client.IsConnected.Should().BeTrue();

            var version = await client.GetVersionAsync();
            version.Should().NotBeNullOrWhiteSpace();

            // Create table & insert data
            var createRes = await client.ExecuteQueryAsync(@"
                CREATE TABLE items (
                    id INTEGER PRIMARY KEY,
                    title VARCHAR,
                    score DOUBLE
                );
            ");
            createRes.IsSuccess.Should().BeTrue();

            var insertRes = await client.ExecuteQueryAsync(@"
                INSERT INTO items VALUES 
                    (1, 'Item One', 42.5),
                    (2, 'Item Two', 88.0);
            ");
            insertRes.IsSuccess.Should().BeTrue();

            // Query data
            var queryRes = await client.ExecuteQueryAsync("SELECT id, title, score FROM items ORDER BY id");
            queryRes.IsSuccess.Should().BeTrue();
            queryRes.Rows.Should().HaveCount(2);
            queryRes.Rows[0][1].Should().Be("Item One");

            // Check metadata
            var tables = await client.ListTablesAsync("main");
            tables.Should().Contain(t => t.Name == "items");

            var columns = await client.ListTableColumnsAsync("items", "main");
            columns.Should().HaveCount(3);
            columns.Should().Contain(c => c.ColumnName == "id");
            columns.Should().Contain(c => c.ColumnName == "title");
            columns.Should().Contain(c => c.ColumnName == "score");
        }
    }

    [Fact]
    public async Task Supabase_Client_InitializesWithSupabaseSchemas()
    {
        var config = new ConnectionConfig
        {
            Name = "Supabase Test",
            ConnectionType = ConnectionType.Supabase,
            Host = "db.xxxxxx.supabase.co",
            DefaultDatabase = "postgres"
        };

        var client = new SupabaseDatabaseClient(config);
        client.ConnectionType.Should().Be(ConnectionType.Supabase);
        var features = await client.GetSupportedFeaturesAsync();
        features.CustomRoutines.Should().BeTrue();
    }

    [Fact]
    public async Task MongoDb_Client_InitializesWithFeatures()
    {
        var config = new ConnectionConfig
        {
            Name = "MongoDB Test",
            ConnectionType = ConnectionType.MongoDb,
            Host = "localhost",
            DefaultDatabase = "admin"
        };

        var client = new MongoDbDatabaseClient(config);
        client.ConnectionType.Should().Be(ConnectionType.MongoDb);
        client.WrapIdentifier("users").Should().Be("users");
        client.EscapeLiteral("test").Should().Be("\"test\"");
        var features = await client.GetSupportedFeaturesAsync();
        features.Truncate.Should().BeTrue();
        features.CustomRoutines.Should().BeFalse();
    }
}
