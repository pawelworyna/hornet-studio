using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Core.Storage.Security;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Tests.Database;

public sealed class BaaSProvidersTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly HornetDbContext _dbContext;
    private readonly MasterPasswordVault _vault;
    private readonly DatabaseClientFactory _factory;

    public BaaSProvidersTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var options = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _dbContext = new HornetDbContext(options);
        _dbContext.Database.EnsureCreated();

        _vault = new MasterPasswordVault();
        _factory = new DatabaseClientFactory();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public async Task Factory_Creates_PocketBase_Client()
    {
        var tempDb = Path.GetTempFileName();
        try
        {
            var config = new ConnectionConfig
            {
                Name = "My PocketBase",
                ConnectionType = ConnectionType.PocketBase,
                DatabasePath = tempDb
            };

            var client = _factory.CreateClient(config);
            client.Should().BeOfType<PocketBaseDatabaseClient>();
            client.ConnectionType.Should().Be(ConnectionType.PocketBase);

            var version = await client.GetVersionAsync();
            version.Should().Contain("PocketBase SQLite");

            await client.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void Factory_Creates_Appwrite_Client()
    {
        var config = new ConnectionConfig
        {
            Name = "My Appwrite",
            ConnectionType = ConnectionType.Appwrite,
            Host = "localhost",
            Port = 3306
        };

        var client = _factory.CreateClient(config);
        client.Should().BeOfType<AppwriteDatabaseClient>();
        client.ConnectionType.Should().Be(ConnectionType.Appwrite);
        client.Config.DefaultDatabase.Should().Be("appwrite");
        client.Config.GetEffectivePort().Should().Be(3306);
    }

    [Fact]
    public void Factory_Creates_AwsAmplify_Client()
    {
        var config = new ConnectionConfig
        {
            Name = "My Amplify Backend",
            ConnectionType = ConnectionType.AwsAmplify,
            Host = "localhost",
            Port = 5432
        };

        var client = _factory.CreateClient(config);
        client.Should().BeOfType<AwsAmplifyDatabaseClient>();
        client.ConnectionType.Should().Be(ConnectionType.AwsAmplify);
        client.Config.DefaultDatabase.Should().Be("amplify");
        client.Config.GetEffectivePort().Should().Be(5432);
    }

    [Fact]
    public async Task Factory_Creates_Firebase_Client_And_Queries_Collections()
    {
        var config = new ConnectionConfig
        {
            Name = "Firebase Emulator",
            ConnectionType = ConnectionType.Firebase,
            Host = "localhost",
            Port = 8080
        };

        var client = _factory.CreateClient(config);
        client.Should().BeOfType<FirebaseDatabaseClient>();
        client.ConnectionType.Should().Be(ConnectionType.Firebase);

        await client.ConnectAsync();
        client.IsConnected.Should().BeTrue();

        var version = await client.GetVersionAsync();
        version.Should().Contain("Firebase Firestore");

        var collections = await client.ListTablesAsync();
        collections.Should().NotBeEmpty();
        collections.Select(c => c.Name).Should().Contain("users");

        var cols = await client.ListTableColumnsAsync("users");
        cols.Should().Contain(c => c.ColumnName == "__id" && c.IsPrimaryKey);

        var data = await client.SelectTopAsync(new SelectTopOptions { Table = "users", Limit = 5 });
        data.Rows.Should().NotBeEmpty();
        data.Rows[0].Should().ContainKey("__id");

        var qResult = await client.ExecuteQueryAsync("firestore.collection('users').get()");
        qResult.IsSuccess.Should().BeTrue();

        await client.DisconnectAsync();
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void ConnectionForm_Applies_Defaults_For_BaaS_Types()
    {
        var form = new ConnectionFormViewModel(_dbContext, _vault, _factory, () => { });

        // PocketBase
        form.ConnectionType = ConnectionType.PocketBase;
        form.IsFileDatabase.Should().BeTrue();
        form.IsPocketBase.Should().BeTrue();
        form.Host.Should().Be("pb_data/data.db");
        form.LabelColor.Should().Be("#B8272C");

        // Appwrite
        form.ConnectionType = ConnectionType.Appwrite;
        form.IsNetworkDatabase.Should().BeTrue();
        form.IsAppwrite.Should().BeTrue();
        form.Port.Should().Be(3306);
        form.Database.Should().Be("appwrite");
        form.LabelColor.Should().Be("#FD366E");

        // Firebase
        form.ConnectionType = ConnectionType.Firebase;
        form.IsFirebase.Should().BeTrue();
        form.Port.Should().Be(8080);
        form.Database.Should().Be("(default)");
        form.LabelColor.Should().Be("#FFCA28");

        // AWS Amplify
        form.ConnectionType = ConnectionType.AwsAmplify;
        form.IsAwsAmplify.Should().BeTrue();
        form.Port.Should().Be(5432);
        form.Database.Should().Be("amplify");
        form.Ssl.Should().BeTrue();
        form.LabelColor.Should().Be("#FF9900");
    }

    [Fact]
    public void ConnectionItemViewModel_Exposes_Correct_Badges_For_BaaS()
    {
        var pbEntity = new SavedConnectionEntity { Name = "PB", ConnectionType = ConnectionType.PocketBase, DatabasePath = "pb_data/data.db" };
        var pbVm = new ConnectionItemViewModel(pbEntity);
        pbVm.DriverBadge.Should().Be("PocketBase");
        pbVm.Subtitle.Should().Be("pb_data/data.db");

        var appwriteEntity = new SavedConnectionEntity { Name = "AW", ConnectionType = ConnectionType.Appwrite, Host = "localhost", Port = 3306, DefaultDatabase = "appwrite", Username = "root" };
        var awVm = new ConnectionItemViewModel(appwriteEntity);
        awVm.DriverBadge.Should().Be("Appwrite");

        var fbEntity = new SavedConnectionEntity { Name = "FB", ConnectionType = ConnectionType.Firebase, Host = "localhost", Port = 8080, DefaultDatabase = "(default)" };
        var fbVm = new ConnectionItemViewModel(fbEntity);
        fbVm.DriverBadge.Should().Be("Firebase");

        var ampEntity = new SavedConnectionEntity { Name = "AMP", ConnectionType = ConnectionType.AwsAmplify, Host = "aurora.rds.amazonaws.com", Port = 5432, DefaultDatabase = "amplify", Username = "postgres" };
        var ampVm = new ConnectionItemViewModel(ampEntity);
        ampVm.DriverBadge.Should().Be("AWS Amplify");
    }

    [Fact]
    public async Task PocketBase_RestMode_Operations()
    {
        var config = new ConnectionConfig
        {
            Name = "PocketBase REST Instance",
            ConnectionType = ConnectionType.PocketBase,
            Host = "http://127.0.0.1:8090",
            Port = 8090
        };

        var client = _factory.CreateClient(config);
        client.Should().BeOfType<PocketBaseDatabaseClient>();
        var pbClient = (PocketBaseDatabaseClient)client;
        pbClient.IsRestMode.Should().BeTrue();

        await pbClient.ConnectAsync();
        pbClient.IsConnected.Should().BeTrue();

        var version = await pbClient.GetVersionAsync();
        version.Should().Contain("PocketBase REST");

        var tables = await pbClient.ListTablesAsync();
        tables.Should().NotBeEmpty();
        tables.Select(t => t.Name).Should().Contain("users");

        var columns = await pbClient.ListTableColumnsAsync("users");
        columns.Should().Contain(c => c.ColumnName == "id" && c.IsPrimaryKey);

        var top = await pbClient.SelectTopAsync(new SelectTopOptions { Table = "users", Limit = 3 });
        top.Rows.Should().NotBeEmpty();
        top.Rows[0].Should().ContainKey("id");

        var queryRes = await pbClient.ExecuteQueryAsync("SELECT * FROM users");
        queryRes.IsSuccess.Should().BeTrue();
        queryRes.AffectedRows.Should().BeGreaterThan(0);

        // Changes test (offline fallback / mock handling)
        var changes = new TableChanges
        {
            Table = "users",
            Inserts = { new TableInsert { Values = { ["username"] = "john_doe" } } },
            Updates = { new TableUpdate { OriginalRow = { ["id"] = "user_1" }, UpdatedValues = { ["username"] = "updated_john" } } },
            Deletes = { new TableDelete { OriginalRow = { ["id"] = "user_2" } } }
        };
        var updateRes = await pbClient.ApplyChangesAsync(changes);
        updateRes.Success.Should().BeTrue();

        await pbClient.DisconnectAsync();
        pbClient.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Firebase_Client_CRUD_And_Streaming()
    {
        var config = new ConnectionConfig
        {
            Name = "Firebase Cloud Firestore",
            ConnectionType = ConnectionType.Firebase,
            Host = "localhost",
            Port = 8080,
            DefaultDatabase = "(default)"
        };

        var client = _factory.CreateClient(config);
        var fbClient = (FirebaseDatabaseClient)client;

        await fbClient.ConnectAsync();

        // Test stream query
        var streamedRows = new List<object?[]>();
        await foreach (var row in fbClient.StreamQueryAsync("SELECT * FROM users"))
        {
            streamedRows.Add(row);
        }
        streamedRows.Should().NotBeEmpty();

        // Test ApplyChanges CRUD
        var changes = new TableChanges
        {
            Table = "users",
            Inserts = { new TableInsert { Values = { ["name"] = "Alice" } } },
            Updates = { new TableUpdate { OriginalRow = { ["__id"] = "users_1" }, UpdatedValues = { ["name"] = "Alice Updated" } } },
            Deletes = { new TableDelete { OriginalRow = { ["__id"] = "users_2" } } }
        };

        var updateRes = await fbClient.ApplyChangesAsync(changes);
        updateRes.Success.Should().BeTrue();

        await fbClient.DisconnectAsync();
    }
}
