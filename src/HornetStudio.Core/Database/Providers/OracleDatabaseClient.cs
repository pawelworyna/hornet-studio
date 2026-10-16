using System.Data;
using Oracle.ManagedDataAccess.Client;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class OracleDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.Oracle;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public OracleDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
        var port = Config.GetEffectivePort();
        var serviceName = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "XE" : Config.DefaultDatabase;

        var connStr = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={serviceName})));User Id={Config.Username ?? "system"};Password={Config.Password ?? ""};";
        
        var conn = new OracleConnection(connStr);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT BANNER FROM v$version WHERE ROWNUM = 1";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "Oracle Database";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = true,
            Comments = true,
            Properties = true,
            Partitions = true,
            Backups = false,
            Restore = false,
            Schemas = true,
            Truncate = true,
            RowIdSupported = true,
            CtidSupported = false
        });
    }

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT name FROM v$database";
        try
        {
            var res = await cmd.ExecuteScalarAsync(ct);
            return [res?.ToString() ?? Config.DefaultDatabase ?? "XE"];
        }
        catch
        {
            return [Config.DefaultDatabase ?? "XE"];
        }
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var schemas = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT username FROM all_users ORDER BY username";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schemas.Add(reader.GetString(0));
        }
        return schemas;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();
        var targetOwner = string.IsNullOrWhiteSpace(schema) ? Config.Username?.ToUpperInvariant() ?? "SYSTEM" : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM all_tables WHERE owner = :owner ORDER BY table_name";
        var param = cmd.CreateParameter();
        param.ParameterName = "owner";
        param.Value = targetOwner;
        cmd.Parameters.Add(param);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0), targetOwner, null, "table"));
        }
        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();
        var targetOwner = string.IsNullOrWhiteSpace(schema) ? Config.Username?.ToUpperInvariant() ?? "SYSTEM" : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT view_name FROM all_views WHERE owner = :owner ORDER BY view_name";
        var param = cmd.CreateParameter();
        param.ParameterName = "owner";
        param.Value = targetOwner;
        cmd.Parameters.Add(param);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0), targetOwner, null, "view"));
        }
        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();
        var targetOwner = string.IsNullOrWhiteSpace(schema) ? Config.Username?.ToUpperInvariant() ?? "SYSTEM" : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, nullable, data_default
            FROM all_tab_columns
            WHERE owner = :owner AND table_name = :tname
            ORDER BY column_id";

        var pOwner = cmd.CreateParameter();
        pOwner.ParameterName = "owner";
        pOwner.Value = targetOwner;
        cmd.Parameters.Add(pOwner);

        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "tname";
        pTable.Value = table.ToUpperInvariant();
        cmd.Parameters.Add(pTable);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ExtendedTableColumn
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                Nullable = string.Equals(reader.GetString(2), "Y", StringComparison.OrdinalIgnoreCase),
                DefaultValue = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return columns;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new List<TableIndex>();
        var targetOwner = string.IsNullOrWhiteSpace(schema) ? Config.Username?.ToUpperInvariant() ?? "SYSTEM" : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT index_name, uniqueness FROM all_indexes WHERE table_owner = :owner AND table_name = :tname";

        var pOwner = cmd.CreateParameter();
        pOwner.ParameterName = "owner";
        pOwner.Value = targetOwner;
        cmd.Parameters.Add(pOwner);

        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "tname";
        pTable.Value = table.ToUpperInvariant();
        cmd.Parameters.Add(pTable);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            indexes.Add(new TableIndex
            {
                Name = reader.GetString(0),
                IsUnique = string.Equals(reader.GetString(1), "UNIQUE", StringComparison.OrdinalIgnoreCase)
            });
        }

        return indexes;
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
        var targetOwner = string.IsNullOrWhiteSpace(schema) ? Config.Username?.ToUpperInvariant() ?? "SYSTEM" : schema.ToUpperInvariant();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT DBMS_METADATA.GET_DDL('TABLE', :tname, :owner) FROM dual";
            var pTable = cmd.CreateParameter();
            pTable.ParameterName = "tname";
            pTable.Value = table.ToUpperInvariant();
            cmd.Parameters.Add(pTable);

            var pOwner = cmd.CreateParameter();
            pOwner.ParameterName = "owner";
            pOwner.Value = targetOwner;
            cmd.Parameters.Add(pOwner);

            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res != DBNull.Value && !string.IsNullOrWhiteSpace(res.ToString()))
                return res.ToString()!;
        }
        catch
        {
            // fallback
        }

        return $"-- DDL dla Oracle {targetOwner}.{table.ToUpperInvariant()}\nCREATE TABLE \"{targetOwner}\".\"{table.ToUpperInvariant()}\" ();";
    }
}
