using System.Data;
using Microsoft.Data.Sqlite;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class SqliteDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.Sqlite;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public SqliteDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var path = Config.DatabasePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = ":memory:";
        }

        var connStr = $"Data Source={path}";
        var conn = new SqliteConnection(connStr);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT sqlite_version()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "SQLite 3";
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
            Schemas = false,
            Truncate = false, // SQLite uses DELETE FROM
            RowIdSupported = true,
            CtidSupported = false
        });
    }

    public override Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["main"]);
    }

    public override Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0), null, "main", "table"));
        }

        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='view' ORDER BY name";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0), null, "main", "view"));
        }

        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var cols = new List<ExtendedTableColumn>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({WrapIdentifier(table)})";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader["name"].ToString() ?? "";
            var type = reader["type"]?.ToString() ?? "TEXT";
            var notNull = Convert.ToInt32(reader["notnull"]) == 1;
            var dflt = reader["dflt_value"] is DBNull ? null : reader["dflt_value"]?.ToString();
            var pk = Convert.ToInt32(reader["pk"]) > 0;

            cols.Add(new ExtendedTableColumn
            {
                ColumnName = name,
                DataType = type,
                Nullable = !notNull,
                DefaultValue = dflt,
                IsPrimaryKey = pk
            });
        }

        return cols;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new List<TableIndex>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"PRAGMA index_list({WrapIdentifier(table)})";

        var indexNames = new List<(string Name, bool Unique)>();
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var name = reader["name"].ToString() ?? "";
                var unique = Convert.ToInt32(reader["unique"]) == 1;
                indexNames.Add((name, unique));
            }
        }

        foreach (var (idxName, unique) in indexNames)
        {
            var idx = new TableIndex
            {
                Name = idxName,
                IsUnique = unique
            };

            using var infoCmd = CurrentConnection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info({WrapIdentifier(idxName)})";
            using var infoReader = await infoCmd.ExecuteReaderAsync(ct);
            while (await infoReader.ReadAsync(ct))
            {
                idx.Columns.Add(infoReader["name"].ToString() ?? "");
            }
            indexes.Add(idx);
        }

        return indexes;
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var keys = new List<TableKey>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({WrapIdentifier(table)})";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            keys.Add(new TableKey
            {
                ConstraintName = $"fk_{table}_{reader["id"]}",
                ColumnName = reader["from"].ToString() ?? "",
                ReferencedTable = reader["table"].ToString() ?? "",
                ReferencedColumn = reader["to"].ToString() ?? "",
                OnUpdate = reader["on_update"].ToString(),
                OnDelete = reader["on_delete"].ToString()
            });
        }

        return keys;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var triggers = new List<TableTrigger>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"SELECT name, sql FROM sqlite_master WHERE type='trigger' AND tbl_name=@tbl";
        cmd.Parameters.Add(new SqliteParameter("@tbl", table));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            triggers.Add(new TableTrigger
            {
                Name = reader.GetString(0),
                Definition = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }

        return triggers;
    }

    public override Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Routine>>([]);
    }

    public override async Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var sql = $"DELETE FROM {WrapIdentifier(table)}";
        await ExecuteQueryAsync(sql, ct);
    }

    public override async Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@tbl";
        cmd.Parameters.Add(new SqliteParameter("@tbl", table));
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? $"CREATE TABLE {WrapIdentifier(table)} ();";
    }
}
