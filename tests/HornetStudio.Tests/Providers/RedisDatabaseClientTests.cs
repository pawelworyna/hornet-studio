using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using Xunit;

namespace HornetStudio.Tests.Providers;

public sealed class RedisDatabaseClientTests
{
    [Fact]
    public void Client_Properties_MatchConnectionConfig()
    {
        var config = new ConnectionConfig
        {
            Name = "Local Redis",
            ConnectionType = ConnectionType.Redis,
            Host = "127.0.0.1",
            Port = 6379,
            DefaultDatabase = "2"
        };

        var client = new RedisDatabaseClient(config);

        client.ConnectionType.Should().Be(ConnectionType.Redis);
        client.Config.Should().BeSameAs(config);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void WrapIdentifier_And_EscapeLiteral_WorkAsExpected()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis };
        var client = new RedisDatabaseClient(config);

        client.WrapIdentifier("my_key").Should().Be("my_key");
        client.EscapeLiteral("hello \"world\"").Should().Be("\"hello \\\"world\\\"\"");
    }

    [Fact]
    public async Task ListDatabasesAsync_ReturnsLogicalDatabases()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis };
        var client = new RedisDatabaseClient(config);

        var dbs = await client.ListDatabasesAsync();

        dbs.Should().HaveCount(16);
        dbs.First().Should().Be("db0");
        dbs.Last().Should().Be("db15");
    }

    [Fact]
    public async Task ListTablesAsync_ReturnsVirtualKeysTable()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis, DefaultDatabase = "1" };
        var client = new RedisDatabaseClient(config);

        var tables = await client.ListTablesAsync();

        tables.Should().HaveCount(1);
        tables[0].Name.Should().Be("keys");
        tables[0].Schema.Should().Be("db1");
    }

    [Fact]
    public async Task ListTableColumnsAsync_ReturnsStandardColumns()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis };
        var client = new RedisDatabaseClient(config);

        var cols = await client.ListTableColumnsAsync("keys");

        cols.Should().HaveCount(5);
        cols.Select(c => c.ColumnName).Should().ContainInOrder("key", "type", "ttl", "length", "value");
        cols.First(c => c.ColumnName == "key").IsPrimaryKey.Should().BeTrue();
    }

    [Fact]
    public async Task GetSupportedFeaturesAsync_ReturnsFeatures()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis };
        var client = new RedisDatabaseClient(config);

        var features = await client.GetSupportedFeaturesAsync();

        features.Truncate.Should().BeTrue();
        features.Schemas.Should().BeFalse();
        features.Comments.Should().BeFalse();
        features.Properties.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateCreateTableSqlAsync_ReturnsHelpfulTemplate()
    {
        var config = new ConnectionConfig { ConnectionType = ConnectionType.Redis };
        var client = new RedisDatabaseClient(config);

        var ddl = await client.GenerateCreateTableSqlAsync("keys", "db0");

        ddl.Should().Contain("Redis Key-Value Store");
        ddl.Should().Contain("db0");
    }

    [Fact]
    public void DatabaseClientFactory_CreatesRedisDatabaseClient()
    {
        var factory = new DatabaseClientFactory();
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.Redis,
            Host = "localhost",
            Port = 6379
        };

        var client = factory.CreateClient(config);

        client.Should().NotBeNull();
        client.Should().BeOfType<RedisDatabaseClient>();
        client.ConnectionType.Should().Be(ConnectionType.Redis);
    }
}
