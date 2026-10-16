using System.Data;
using DuckDB.NET.Data;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class DuckDbDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.DuckDb;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public DuckDbDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var path = Config.DatabasePath;
        if (string.IsNullOrWhiteSpace(path) || path == ":memory:")
        {
            path = ""; // In-memory database in DuckDB
        }

        var connStr = string.IsNullOrWhiteSpace(path) ? "Data Source=:memory:" : $"Data Source={path}";
        var conn = new DuckDBConnection(connStr);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT version()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "DuckDB";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = false,
            Comments = false,
            Properties = true,
            Partitions = false,
            Backups = true,
            Restore = true,
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
        cmd.CommandText = "SELECT current_database()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return [res?.ToString() ?? "memory"];
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var schemas = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ('information_schema', 'pg_catalog') ORDER BY schema_name";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schemas.Add(reader.GetString(0));
        }
        return schemas.Count > 0 ? schemas : ["main"];
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema) ? "main" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = $schema AND table_type = 'BASE TABLE' ORDER BY table_name";
        
        var param = cmd.CreateParameter();
        param.ParameterName = "schema";
        param.Value = targetSchema;
        cmd.Parameters.Add(param);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0), targetSchema, null, "table"));
        }
        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema) ? "main" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = $schema AND table_type = 'VIEW' ORDER BY table_name";

        var param = cmd.CreateParameter();
        param.ParameterName = "schema";
        param.Value = targetSchema;
        cmd.Parameters.Add(param);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0), targetSchema, null, "view"));
        }
        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();
        var targetSchema = string.IsNullOrWhiteSpace(schema) ? "main" : schema;

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = $schema AND table_name = $table
            ORDER BY ordinal_position";

        var pSchema = cmd.CreateParameter();
        pSchema.ParameterName = "schema";
        pSchema.Value = targetSchema;
        cmd.Parameters.Add(pSchema);

        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "table";
        pTable.Value = table;
        cmd.Parameters.Add(pTable);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);
            var defaultValue = reader.IsDBNull(3) ? null : reader.GetString(3);

            columns.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = dataType,
                Nullable = isNullable,
                DefaultValue = defaultValue
            });
        }

        return columns;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new List<TableIndex>();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT index_name, is_unique FROM duckdb_indexes() WHERE table_name = $table";
            var pTable = cmd.CreateParameter();
            pTable.ParameterName = "table";
            pTable.Value = table;
            cmd.Parameters.Add(pTable);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                indexes.Add(new TableIndex
                {
                    Name = reader.GetString(0),
                    IsUnique = reader.GetBoolean(1),
                    IsPrimaryKey = false
                });
            }
        }
        catch
        {
            // DuckDB versions might vary in index reflection function
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
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT sql FROM duckdb_tables() WHERE table_name = $table";
            var p = cmd.CreateParameter();
            p.ParameterName = "table";
            p.Value = table;
            cmd.Parameters.Add(p);
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res != DBNull.Value && !string.IsNullOrWhiteSpace(res.ToString()))
                return res.ToString()!;
        }
        catch
        {
            // fallback
        }

        return $"CREATE TABLE {WrapIdentifier(table)} ();";
    }
}
