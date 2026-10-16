using System.Data;
using ClickHouse.Client.ADO;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class ClickHouseDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.ClickHouse;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public ClickHouseDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
        var port = Config.GetEffectivePort();
        var database = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "default" : Config.DefaultDatabase;
        var user = string.IsNullOrWhiteSpace(Config.Username) ? "default" : Config.Username;
        var password = Config.Password ?? "";

        var connStr = $"Host={host};Port={port};Database={database};Username={user};Password={password};";
        var conn = new ClickHouseConnection(connStr);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT version()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return $"ClickHouse {res}";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = false,
            Comments = true,
            Properties = true,
            Partitions = true,
            Backups = false,
            Restore = false,
            Schemas = false,
            Truncate = true,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var databases = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SHOW DATABASES";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            databases.Add(reader.GetString(0));
        }
        return databases.Count > 0 ? databases : ["default"];
    }

    public override Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();
        var db = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "default" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"SELECT name FROM system.tables WHERE database = '{db.Replace("'", "''")}' AND is_temporary = 0 AND engine NOT LIKE '%View%' ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0), db, null, "table"));
        }
        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();
        var db = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "default" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"SELECT name FROM system.tables WHERE database = '{db.Replace("'", "''")}' AND is_temporary = 0 AND engine LIKE '%View%' ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0), db, null, "view"));
        }
        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();
        var db = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "default" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"SELECT name, type, default_expression, position, is_in_primary_key FROM system.columns WHERE database = '{db.Replace("'", "''")}' AND table = '{table.Replace("'", "''")}' ORDER BY position";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ExtendedTableColumn
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                DefaultValue = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsPrimaryKey = Convert.ToBoolean(reader.GetValue(4)),
                Nullable = reader.GetString(1).StartsWith("Nullable(", StringComparison.OrdinalIgnoreCase)
            });
        }

        return columns;
    }

    public override Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableIndex>>([]);
    }

    public override Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableKey>>([]);
    }

    public override Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableTrigger>>([]);
    }

    public override Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Routine>>([]);
    }

    public override async Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "default" : schema;
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = $"SHOW CREATE TABLE {WrapIdentifier(db)}.{WrapIdentifier(table)}";
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res != DBNull.Value && !string.IsNullOrWhiteSpace(res.ToString()))
                return res.ToString()!;
        }
        catch
        {
            // fallback
        }

        return $"CREATE TABLE {WrapIdentifier(db)}.{WrapIdentifier(table)} ();";
    }
}
