using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class SapHanaDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.SapHana;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public SapHanaDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public override string BuildPagingClause(int limit, int offset)
    {
        if (offset > 0)
        {
            return $" LIMIT {limit} OFFSET {offset}";
        }
        return $" LIMIT {limit}";
    }

    public string BuildConnectionString()
    {
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            return Config.ConnectionString;
        }

        var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
        var port = Config.GetEffectivePort();
        var db = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "HDB" : Config.DefaultDatabase;
        var uid = Config.Username ?? "SYSTEM";
        var pwd = Config.Password ?? string.Empty;

        return $"Driver={{HDBODBC}};ServerNode={host}:{port};CS={db};UID={uid};PWD={pwd};";
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var connStr = BuildConnectionString();
        var conn = new OdbcConnection(connStr);
        try
        {
            await conn.OpenAsync(ct);
            CurrentConnection = conn;
        }
        catch (Exception ex)
        {
            conn.Dispose();
            throw new InvalidOperationException(
                $"Failed to connect to SAP HANA ({Config.Host}:{Config.GetEffectivePort()} / {Config.DefaultDatabase}): {ex.Message}. " +
                "Please verify that SAP HANA HDBODBC driver is installed and the HANA database instance is running.", ex);
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT VERSION FROM SYS.M_DATABASE";
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && res != DBNull.Value)
            {
                return $"SAP HANA {res}";
            }
        }
        catch
        {
            // Fallback
        }

        return "SAP HANA In-Memory Database";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = true,
            Comments = true,
            Properties = true,
            Partitions = true,
            Backups = true,
            Restore = true,
            Schemas = true,
            Truncate = true,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public override async Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        string cleanSql = sql.Trim().TrimEnd(';');
        return await ExecuteQueryAsync($"EXPLAIN PLAN FOR {cleanSql}", ct);
    }

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT DATABASE_NAME FROM SYS.M_DATABASES";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Fallback
        }

        if (list.Count == 0)
        {
            list.Add(Config.DefaultDatabase ?? "HDB");
        }
        return list;
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT SCHEMA_NAME FROM SYS.SCHEMAS WHERE SCHEMA_NAME NOT LIKE '_SYS_%' ORDER BY SCHEMA_NAME";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Fallback
        }

        if (list.Count == 0)
        {
            list.Add(Config.Username?.ToUpperInvariant() ?? "SYSTEM");
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT TABLE_NAME FROM SYS.TABLES WHERE SCHEMA_NAME = ? ORDER BY TABLE_NAME";
        var p = cmd.CreateParameter();
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0), targetSchema, Config.DefaultDatabase, "table"));
        }
        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT VIEW_NAME FROM SYS.VIEWS WHERE SCHEMA_NAME = ? ORDER BY VIEW_NAME";
        var p = cmd.CreateParameter();
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0), targetSchema, Config.DefaultDatabase, "view"));
        }
        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        var pks = await GetPrimaryKeyColumnNames(table, targetSchema, ct);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT COLUMN_NAME, DATA_TYPE_NAME, IS_NULLABLE, DEFAULT_VALUE, LENGTH
            FROM SYS.TABLE_COLUMNS
            WHERE SCHEMA_NAME = ? AND TABLE_NAME = ?
            ORDER BY POSITION";

        var pSchema = cmd.CreateParameter();
        pSchema.Value = targetSchema;
        cmd.Parameters.Add(pSchema);

        var pTable = cmd.CreateParameter();
        pTable.Value = table.ToUpperInvariant();
        cmd.Parameters.Add(pTable);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullableStr = reader.GetString(2);
            var defVal = reader.IsDBNull(3) ? null : reader.GetString(3);
            var len = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));

            columns.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = dataType,
                Nullable = string.Equals(isNullableStr, "TRUE", StringComparison.OrdinalIgnoreCase),
                DefaultValue = defVal,
                MaxLength = len,
                IsPrimaryKey = pks.Contains(colName)
            });
        }

        return columns;
    }

    private async Task<HashSet<string>> GetPrimaryKeyColumnNames(string table, string schema, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = @"
                SELECT COLUMN_NAME
                FROM SYS.CONSTRAINTS
                WHERE SCHEMA_NAME = ?
                  AND TABLE_NAME = ?
                  AND IS_PRIMARY_KEY = 'TRUE'";

            var p1 = cmd.CreateParameter(); p1.Value = schema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                set.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Ignore catalog errors
        }
        return set;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new List<TableIndex>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT INDEX_NAME, CONSTRAINT FROM SYS.INDEXES WHERE SCHEMA_NAME = ? AND TABLE_NAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var indName = reader.GetString(0);
                var constraint = reader.IsDBNull(1) ? "" : reader.GetString(1);

                indexes.Add(new TableIndex
                {
                    Name = indName,
                    IsUnique = string.Equals(constraint, "UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(constraint, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase),
                    IsPrimaryKey = string.Equals(constraint, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // Ignore catalog errors
        }

        return indexes;
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var keys = new List<TableKey>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = @"
                SELECT CONSTRAINT_NAME, REFERENCED_TABLE_NAME
                FROM SYS.REFERENTIAL_CONSTRAINTS
                WHERE SCHEMA_NAME = ? AND TABLE_NAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                keys.Add(new TableKey
                {
                    ConstraintName = reader.GetString(0),
                    ReferencedTable = reader.GetString(1)
                });
            }
        }
        catch
        {
            // Ignore catalog errors
        }

        return keys;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var triggers = new List<TableTrigger>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT TRIGGER_NAME FROM SYS.TRIGGERS WHERE SCHEMA_NAME = ? AND SUBJECT_TABLE_NAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                triggers.Add(new TableTrigger { Name = reader.GetString(0) });
            }
        }
        catch
        {
            // Ignore catalog errors
        }

        return triggers;
    }

    public override async Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var routines = new List<Routine>();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT PROCEDURE_NAME, SCHEMA_NAME FROM SYS.PROCEDURES WHERE SCHEMA_NAME NOT LIKE '_SYS_%'";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                routines.Add(new Routine
                {
                    Name = reader.GetString(0),
                    RoutineType = "PROCEDURE",
                    Schema = reader.GetString(1)
                });
            }
        }
        catch
        {
            // Ignore catalog errors
        }

        return routines;
    }

    public override Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "SYSTEM")
            : schema.ToUpperInvariant();

        return Task.FromResult($"-- DDL dla SAP HANA {targetSchema}.{table.ToUpperInvariant()}\nCREATE COLUMN TABLE \"{targetSchema}\".\"{table.ToUpperInvariant()}\" ();");
    }
}
