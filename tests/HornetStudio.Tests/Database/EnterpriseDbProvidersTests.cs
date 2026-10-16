using System;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Database.Providers;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Desktop.ViewModels;
using Xunit;

namespace HornetStudio.Tests.Database;

public class EnterpriseDbProvidersTests
{
    private readonly DatabaseClientFactory _factory = new();

    [Fact]
    public async Task Db2Client_PropertiesAndSqlDialect_AreConfiguredCorrectly()
    {
        var config = new ConnectionConfig
        {
            Name = "Production DB2",
            ConnectionType = ConnectionType.Db2,
            Host = "db2.enterprise.internal",
            Port = 50000,
            DefaultDatabase = "PRODDB",
            Username = "db2user",
            Password = "SecretPassword123"
        };

        var client = _factory.CreateClient(config) as Db2DatabaseClient;
        client.Should().NotBeNull();
        client!.ConnectionType.Should().Be(ConnectionType.Db2);

        // Quoting
        client.WrapIdentifier("Employees").Should().Be("\"Employees\"");
        client.WrapIdentifier("Order\"Details").Should().Be("\"Order\"\"Details\"");

        // Paging
        client.BuildPagingClause(50, 0).Should().Be(" FETCH FIRST 50 ROWS ONLY");
        client.BuildPagingClause(25, 100).Should().Be(" OFFSET 100 ROWS FETCH FIRST 25 ROWS ONLY");

        // Connection String
        var connStr = client.BuildConnectionString();
        connStr.Should().Contain("Driver={IBM DB2 ODBC DRIVER}");
        connStr.Should().Contain("Hostname=db2.enterprise.internal");
        connStr.Should().Contain("Port=50000");
        connStr.Should().Contain("Database=PRODDB");
        connStr.Should().Contain("Uid=db2user");

        // DDL
        var ddl = await client.GenerateCreateTableSqlAsync("CUSTOMERS", "HR");
        ddl.Should().Contain("CREATE TABLE \"HR\".\"CUSTOMERS\"");
    }

    [Fact]
    public async Task Db2Client_SupportedFeatures_ReflectEnterpriseCapabilities()
    {
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.Db2
        };
        var client = new Db2DatabaseClient(config);
        var features = await client.GetSupportedFeaturesAsync();

        features.Schemas.Should().BeTrue();
        features.Truncate.Should().BeTrue();
        features.CustomRoutines.Should().BeTrue();
        features.Comments.Should().BeTrue();
        features.Partitions.Should().BeTrue();
        features.Backups.Should().BeTrue();
        features.Restore.Should().BeTrue();
        features.RowIdSupported.Should().BeFalse();
        features.CtidSupported.Should().BeFalse();
    }

    [Fact]
    public async Task SapHanaClient_PropertiesAndSqlDialect_AreConfiguredCorrectly()
    {
        var config = new ConnectionConfig
        {
            Name = "SAP HANA In-Memory",
            ConnectionType = ConnectionType.SapHana,
            Host = "hana-server.corp",
            Port = 30015,
            DefaultDatabase = "TENANT1",
            Username = "SYSTEM",
            Password = "HanaPassword!"
        };

        var client = _factory.CreateClient(config) as SapHanaDatabaseClient;
        client.Should().NotBeNull();
        client!.ConnectionType.Should().Be(ConnectionType.SapHana);

        // Quoting
        client.WrapIdentifier("V_SALES_ORDER").Should().Be("\"V_SALES_ORDER\"");

        // Paging
        client.BuildPagingClause(100, 0).Should().Be(" LIMIT 100");
        client.BuildPagingClause(50, 200).Should().Be(" LIMIT 50 OFFSET 200");

        // Connection String
        var connStr = client.BuildConnectionString();
        connStr.Should().Contain("Driver={HDBODBC}");
        connStr.Should().Contain("ServerNode=hana-server.corp:30015");
        connStr.Should().Contain("CS=TENANT1");
        connStr.Should().Contain("UID=SYSTEM");

        // DDL
        var ddl = await client.GenerateCreateTableSqlAsync("SALES", "ERP");
        ddl.Should().Contain("CREATE COLUMN TABLE \"ERP\".\"SALES\"");
    }

    [Fact]
    public async Task SapHanaClient_SupportedFeatures_ReflectCapabilities()
    {
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.SapHana
        };
        var client = new SapHanaDatabaseClient(config);
        var features = await client.GetSupportedFeaturesAsync();

        features.Schemas.Should().BeTrue();
        features.Truncate.Should().BeTrue();
        features.CustomRoutines.Should().BeTrue();
        features.Properties.Should().BeTrue();
    }

    [Fact]
    public async Task MsAccessClient_PropertiesAndSqlDialect_AreConfiguredCorrectly()
    {
        var config = new ConnectionConfig
        {
            Name = "Access Inventory",
            ConnectionType = ConnectionType.MsAccess,
            DatabasePath = @"C:\Data\Northwind.accdb"
        };

        var client = _factory.CreateClient(config) as MsAccessDatabaseClient;
        client.Should().NotBeNull();
        client!.ConnectionType.Should().Be(ConnectionType.MsAccess);

        // Path resolution
        client.GetFilePath().Should().Be(@"C:\Data\Northwind.accdb");

        // Quoting (square brackets for Access)
        client.WrapIdentifier("Order Details").Should().Be("[Order Details]");
        client.WrapIdentifier("Order]Test").Should().Be("[Order]]Test]");

        // Connection String
        var odbcStr = client.BuildConnectionString();
        odbcStr.Should().Contain("Driver={Microsoft Access Driver (*.mdb, *.accdb)}");
        odbcStr.Should().Contain(@"DBQ=C:\Data\Northwind.accdb");

        var oleDbStr = client.BuildOleDbConnectionString();
        oleDbStr.Should().Contain("Provider=Microsoft.ACE.OLEDB.16.0");
        oleDbStr.Should().Contain(@"Data Source=C:\Data\Northwind.accdb");

        // DDL
        var ddl = await client.GenerateCreateTableSqlAsync("Products");
        ddl.Should().Contain("CREATE TABLE [Products]");
        ddl.Should().Contain("[Id] AUTOINCREMENT PRIMARY KEY");
    }

    [Fact]
    public async Task MsAccessClient_SupportedFeatures_ReflectFileEngine()
    {
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.MsAccess,
            DatabasePath = "test.accdb"
        };
        var client = new MsAccessDatabaseClient(config);
        var features = await client.GetSupportedFeaturesAsync();

        features.Schemas.Should().BeFalse();
        features.Truncate.Should().BeFalse();
        features.CustomRoutines.Should().BeFalse();
        features.Properties.Should().BeTrue();
        features.Backups.Should().BeTrue();

        var version = await client.GetVersionAsync();
        version.Should().Contain("Microsoft Access Database Engine (.ACCDB)");

        var databases = await client.ListDatabasesAsync();
        databases.Should().Contain("test.accdb");

        var schemas = await client.ListSchemasAsync();
        schemas.Should().Contain("default");
    }

    [Fact]
    public void ConnectionItemViewModel_DisplaysEnterpriseBadgesAndSubtitles()
    {
        var db2Entity = new SavedConnectionEntity
        {
            Name = "Corporate DB2",
            ConnectionType = ConnectionType.Db2,
            Host = "db2.corp",
            Port = 50000,
            DefaultDatabase = "CORPDB",
            Username = "db2admin"
        };
        var db2Vm = new ConnectionItemViewModel(db2Entity);
        db2Vm.DriverBadge.Should().Be("IBM DB2");
        db2Vm.Subtitle.Should().Be("db2admin@db2.corp:50000/CORPDB");

        var hanaEntity = new SavedConnectionEntity
        {
            Name = "Corporate HANA",
            ConnectionType = ConnectionType.SapHana,
            Host = "hana.corp",
            Port = 30015,
            DefaultDatabase = "HDB",
            Username = "SYSTEM"
        };
        var hanaVm = new ConnectionItemViewModel(hanaEntity);
        hanaVm.DriverBadge.Should().Be("SAP HANA");
        hanaVm.Subtitle.Should().Be("SYSTEM@hana.corp:30015/HDB");

        var accessEntity = new SavedConnectionEntity
        {
            Name = "Local Inventory",
            ConnectionType = ConnectionType.MsAccess,
            DatabasePath = @"C:\Databases\Inventory.accdb"
        };
        var accessVm = new ConnectionItemViewModel(accessEntity);
        accessVm.DriverBadge.Should().Be("MS Access");
        accessVm.Subtitle.Should().Be(@"C:\Databases\Inventory.accdb");

        var fbEntity = new SavedConnectionEntity
        {
            Name = "Production Firebird",
            ConnectionType = ConnectionType.Firebird,
            Host = "firebird.internal",
            Port = 3050,
            DefaultDatabase = "employee.fdb",
            Username = "SYSDBA"
        };
        var fbVm = new ConnectionItemViewModel(fbEntity);
        fbVm.DriverBadge.Should().Be("Firebird");
        fbVm.Subtitle.Should().Be("SYSDBA@firebird.internal:3050/employee.fdb");
    }

    [Fact]
    public async Task FirebirdClient_PropertiesAndSqlDialect_AreConfiguredCorrectly()
    {
        var config = new ConnectionConfig
        {
            Name = "Company Firebird",
            ConnectionType = ConnectionType.Firebird,
            Host = "firebird.internal",
            Port = 3050,
            DefaultDatabase = "employee.fdb",
            Username = "SYSDBA",
            Password = "masterkey"
        };

        var client = _factory.CreateClient(config) as FirebirdDatabaseClient;
        client.Should().NotBeNull();
        client!.ConnectionType.Should().Be(ConnectionType.Firebird);

        // Quoting
        client.WrapIdentifier("EMPLOYEE").Should().Be("\"EMPLOYEE\"");
        client.WrapIdentifier("Table\"Name").Should().Be("\"Table\"\"Name\"");

        // Paging
        client.BuildPagingClause(50, 0).Should().Be(" FETCH FIRST 50 ROWS ONLY");
        client.BuildPagingClause(25, 100).Should().Be(" OFFSET 100 ROWS FETCH NEXT 25 ROWS ONLY");

        // Connection String
        var connStr = client.BuildConnectionString();
        connStr.Should().ContainEquivalentOf("SYSDBA");
        connStr.Should().ContainEquivalentOf("3050");
        connStr.Should().ContainEquivalentOf("employee.fdb");
        connStr.Should().ContainEquivalentOf("firebird.internal");

        // Schemas
        var schemas = await client.ListSchemasAsync();
        schemas.Should().Contain("DEFAULT");

        // Supported Features
        var features = await client.GetSupportedFeaturesAsync();
        features.CustomRoutines.Should().BeTrue();
        features.Truncate.Should().BeTrue();
        features.Backups.Should().BeTrue();
        features.Schemas.Should().BeFalse();
        features.RowIdSupported.Should().BeFalse();
    }
}
