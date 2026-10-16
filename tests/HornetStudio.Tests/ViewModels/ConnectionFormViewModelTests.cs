using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Security;
using HornetStudio.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HornetStudio.Tests.ViewModels;

public class ConnectionFormViewModelTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly string _tempVaultDir;
    private readonly HornetDbContext _db;

    public ConnectionFormViewModelTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"hornet_cf_test_{Guid.NewGuid()}.db");
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"hornet_cf_vault_{Guid.NewGuid()}");

        var opts = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite($"Data Source={_tempDbFile}")
            .Options;
        _db = new HornetDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }

    [Fact]
    public void InitialState_DefaultsToPostgreSQL()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType.Should().Be(ConnectionType.PostgreSql);
        vm.IsPostgres.Should().BeTrue();
        vm.IsMySql.Should().BeFalse();
        vm.IsSqlServer.Should().BeFalse();
        vm.IsSqlite.Should().BeFalse();
        vm.IsNotSqlite.Should().BeTrue();
        vm.Port.Should().Be(5432);
        vm.Database.Should().Be("postgres");
        vm.Username.Should().Be("postgres");
        vm.Host.Should().Be("localhost");
    }

    [Fact]
    public void SwitchingToMySql_UpdatesDedicatedOptionsAndDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        // Simulate user clicking MySQL radio button in UI
        vm.IsMySql = true;

        vm.ConnectionType.Should().Be(ConnectionType.MySql);
        vm.IsMySql.Should().BeTrue();
        vm.IsPostgres.Should().BeFalse();
        vm.IsSqlServer.Should().BeFalse();
        vm.IsSqlite.Should().BeFalse();
        vm.IsNotSqlite.Should().BeTrue();
        vm.Port.Should().Be(3306);
        vm.Database.Should().Be("mysql");
        vm.Username.Should().Be("root");
        vm.Name.Should().Be("MySQL Local");
    }

    [Fact]
    public void SwitchingToSqlServer_UpdatesDedicatedOptionsAndDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        // Simulate user clicking SQL Server radio button in UI
        vm.IsSqlServer = true;

        vm.ConnectionType.Should().Be(ConnectionType.SqlServer);
        vm.IsSqlServer.Should().BeTrue();
        vm.IsPostgres.Should().BeFalse();
        vm.IsMySql.Should().BeFalse();
        vm.IsSqlite.Should().BeFalse();
        vm.IsNotSqlite.Should().BeTrue();
        vm.Port.Should().Be(1433);
        vm.Database.Should().Be("master");
        vm.Username.Should().Be("sa");
        vm.Name.Should().Be("SQL Server Local");
    }

    [Fact]
    public void SwitchingToSqlite_UpdatesToDedicatedFileOptions()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        // Simulate user clicking SQLite radio button in UI
        vm.IsSqlite = true;

        vm.ConnectionType.Should().Be(ConnectionType.Sqlite);
        vm.IsSqlite.Should().BeTrue();
        vm.IsNotSqlite.Should().BeFalse();
        vm.IsPostgres.Should().BeFalse();
        vm.IsMySql.Should().BeFalse();
        vm.IsSqlServer.Should().BeFalse();
        vm.Port.Should().Be(0);
        vm.Database.Should().BeEmpty();
        vm.Username.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
        vm.Host.Should().Be("data.db");
        vm.Name.Should().Be("SQLite Database");

        // Simulate user selecting file
        vm.SetSqliteDatabasePath(@"C:\data\my_test.db");
        vm.Host.Should().Be(@"C:\data\my_test.db");
        vm.Name.Should().Be("my_test");

        var config = vm.BuildConnectionConfig();
        config.DatabasePath.Should().Be(@"C:\data\my_test.db");
        config.ConnectionType.Should().Be(ConnectionType.Sqlite);
    }

    [Fact]
    public void SqlServer_WindowsAuthentication_ClearsCredentialsInConfig()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();

        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });
        vm.IsSqlServer = true;
        vm.Username = "admin";
        vm.Password = "secret";
        vm.UseWindowsAuth = true;

        var config = vm.BuildConnectionConfig();
        config.Username.Should().BeNull();
        config.Password.Should().BeNull();
    }

    [Fact]
    public void AvailableDatabaseTypes_ContainsAllSupportedEngines()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.AvailableDatabaseTypes.Should().Contain([
            ConnectionType.PostgreSql,
            ConnectionType.Supabase,
            ConnectionType.MongoDb,
            ConnectionType.MySql,
            ConnectionType.MariaDB,
            ConnectionType.SqlServer,
            ConnectionType.Sqlite,
            ConnectionType.DuckDb,
            ConnectionType.Oracle,
            ConnectionType.ClickHouse,
            ConnectionType.CockroachDb,
            ConnectionType.Redshift,
            ConnectionType.LibSql
        ]);
    }

    [Fact]
    public void SwitchingToDuckDb_ConfiguresFileDatabase()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.DuckDb;

        vm.IsFileDatabase.Should().BeTrue();
        vm.IsNetworkDatabase.Should().BeFalse();
        vm.Port.Should().Be(0);
        vm.Host.Should().Be("analytics.duckdb");
        vm.Name.Should().Be("DuckDB Analytics");

        vm.SetSqliteDatabasePath(@"C:\data\analytics.duckdb");
        var config = vm.BuildConnectionConfig();
        config.ConnectionType.Should().Be(ConnectionType.DuckDb);
        config.DatabasePath.Should().Be(@"C:\data\analytics.duckdb");
    }

    [Fact]
    public void SwitchingToOracle_ConfiguresOracleDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.Oracle;

        vm.IsOracle.Should().BeTrue();
        vm.Port.Should().Be(1521);
        vm.Database.Should().Be("XE");
        vm.Username.Should().Be("SYSTEM");
        vm.DatabaseFieldLabel.Should().Contain("Service Name");
    }

    [Fact]
    public void SwitchingToClickHouse_ConfiguresClickHouseDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.ClickHouse;

        vm.IsClickHouse.Should().BeTrue();
        vm.Port.Should().Be(8123);
        vm.Database.Should().Be("default");
        vm.Username.Should().Be("default");
    }

    [Fact]
    public void SwitchingToSupabase_ConfiguresSupabaseDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.Supabase;

        vm.IsSupabase.Should().BeTrue();
        vm.IsPostgres.Should().BeTrue();
        vm.Port.Should().Be(5432);
        vm.Database.Should().Be("postgres");
        vm.Username.Should().Be("postgres");
        vm.Ssl.Should().BeTrue();
        vm.TrustServerCertificate.Should().BeTrue();
        vm.LabelColor.Should().Be("#3ECF8E");
    }

    [Fact]
    public void SwitchingToMongoDb_ConfiguresMongoDbDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.MongoDb;

        vm.IsMongoDb.Should().BeTrue();
        vm.Port.Should().Be(27017);
        vm.Database.Should().Be("admin");
        vm.Username.Should().BeEmpty();
        vm.LabelColor.Should().Be("#00ED64");
    }

    [Fact]
    public void SwitchingToRedis_ConfiguresRedisDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.Redis;

        vm.IsRedis.Should().BeTrue();
        vm.Port.Should().Be(6379);
        vm.Database.Should().Be("0");
        vm.DatabaseFieldLabel.Should().Contain("0 - 15");
        vm.Username.Should().BeEmpty();
        vm.LabelColor.Should().Be("#DC2626");
        vm.AvailableDatabaseTypes.Should().Contain(ConnectionType.Redis);
    }

    [Fact]
    public void SwitchingToDb2_ConfiguresDb2Defaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.Db2;

        vm.IsDb2.Should().BeTrue();
        vm.IsFileDatabase.Should().BeFalse();
        vm.IsNetworkDatabase.Should().BeTrue();
        vm.Port.Should().Be(50000);
        vm.Database.Should().Be("SAMPLE");
        vm.Username.Should().Be("db2admin");
        vm.LabelColor.Should().Be("#0062FF");
        vm.DatabaseFieldLabel.Should().Contain("SAMPLE");
        vm.AvailableDatabaseTypes.Should().Contain(ConnectionType.Db2);
    }

    [Fact]
    public void SwitchingToSapHana_ConfiguresSapHanaDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.SapHana;

        vm.IsSapHana.Should().BeTrue();
        vm.IsFileDatabase.Should().BeFalse();
        vm.IsNetworkDatabase.Should().BeTrue();
        vm.Port.Should().Be(30015);
        vm.Database.Should().Be("HDB");
        vm.Username.Should().Be("SYSTEM");
        vm.LabelColor.Should().Be("#008FD3");
        vm.DatabaseFieldLabel.Should().Contain("HDB");
        vm.AvailableDatabaseTypes.Should().Contain(ConnectionType.SapHana);
    }

    [Fact]
    public void SwitchingToMsAccess_ConfiguresMsAccessDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.MsAccess;

        vm.IsMsAccess.Should().BeTrue();
        vm.IsFileDatabase.Should().BeTrue();
        vm.IsNetworkDatabase.Should().BeFalse();
        vm.Port.Should().Be(0);
        vm.Host.Should().Be("Database.accdb");
        vm.FileDatabaseHeader.Should().Contain("Access");
        vm.AvailableDatabaseTypes.Should().Contain(ConnectionType.MsAccess);

        // Build config verify
        var config = vm.BuildConnectionConfig();
        config.DatabasePath.Should().Be("Database.accdb");
    }

    [Fact]
    public void SwitchingToFirebird_ConfiguresFirebirdDefaults()
    {
        var vault = new MasterPasswordVault(_tempVaultDir);
        var mockFactory = new Mock<IDatabaseClientFactory>();
        var vm = new ConnectionFormViewModel(_db, vault, mockFactory.Object, () => { });

        vm.ConnectionType = ConnectionType.Firebird;

        vm.IsFirebird.Should().BeTrue();
        vm.IsFileDatabase.Should().BeFalse();
        vm.IsNetworkDatabase.Should().BeTrue();
        vm.Port.Should().Be(3050);
        vm.Database.Should().Be("employee.fdb");
        vm.Username.Should().Be("SYSDBA");
        vm.Password.Should().Be("masterkey");
        vm.LabelColor.Should().Be("#E25822");
        vm.DatabaseFieldLabel.Should().Contain("employee.fdb");
        vm.AvailableDatabaseTypes.Should().Contain(ConnectionType.Firebird);

        var config = vm.BuildConnectionConfig();
        config.ConnectionType.Should().Be(ConnectionType.Firebird);
        config.Port.Should().Be(3050);
        config.DefaultDatabase.Should().Be("employee.fdb");
    }
}
