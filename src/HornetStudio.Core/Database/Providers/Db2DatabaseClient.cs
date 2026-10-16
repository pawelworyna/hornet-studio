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

public class Db2DatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.Db2;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public Db2DatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public override string BuildPagingClause(int limit, int offset)
    {
        if (offset > 0)
        {
            return $" OFFSET {offset} ROWS FETCH FIRST {limit} ROWS ONLY";
        }
        return $" FETCH FIRST {limit} ROWS ONLY";
    }

    public string BuildConnectionString()
    {
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            return Config.ConnectionString;
        }

        var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
        var port = Config.GetEffectivePort();
        var db = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "SAMPLE" : Config.DefaultDatabase;
        var uid = Config.Username ?? "db2admin";
        var pwd = Config.Password ?? string.Empty;

        return $"Driver={{IBM DB2 ODBC DRIVER}};Database={db};Hostname={host};Port={port};Protocol=TCPIP;Uid={uid};Pwd={pwd};";
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
                $"Failed to connect to IBM DB2 ({Config.Host}:{Config.GetEffectivePort()} / {Config.DefaultDatabase}): {ex.Message}. " +
                "Please verify that IBM DB2 ODBC Driver (clidriver/ODBC) is installed and network connectivity is available.", ex);
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT SERVICE_LEVEL, FIXPACK_NUM FROM SYSIBMADM.ENV_INST_INFO FETCH FIRST 1 ROWS ONLY";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var level = reader.IsDBNull(0) ? "IBM DB2" : reader.GetString(0);
                var fixpack = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetString(1) : "";
                return string.IsNullOrWhiteSpace(fixpack) ? level : $"{level} (Fixpack {fixpack})";
            }
        }
        catch
        {
            // Fallback
        }

        return "IBM DB2 LUW Database Server";
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
        return await ExecuteQueryAsync($"EXPLAIN ALL FOR {cleanSql}", ct);
    }

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var databases = new List<string>();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT DB_NAME FROM SYSIBMADM.ENV_SYS_INFO";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                databases.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Fallback
        }

        if (databases.Count == 0)
        {
            databases.Add(Config.DefaultDatabase ?? "SAMPLE");
        }
        return databases;
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var schemas = new List<string>();
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT SCHEMANAME FROM SYSCAT.SCHEMATA WHERE SCHEMANAME NOT LIKE 'SYS%' ORDER BY SCHEMANAME";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                schemas.Add(reader.GetString(0).Trim());
            }
        }
        catch
        {
            // Fallback
        }

        if (schemas.Count == 0)
        {
            schemas.Add(Config.Username?.ToUpperInvariant() ?? "DB2ADMIN");
        }
        return schemas;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT TABNAME FROM SYSCAT.TABLES WHERE TABSCHEMA = ? AND TYPE = 'T' ORDER BY TABNAME";
        var p = cmd.CreateParameter();
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableOrView(reader.GetString(0).Trim(), targetSchema, Config.DefaultDatabase, "table"));
        }
        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT TABNAME FROM SYSCAT.TABLES WHERE TABSCHEMA = ? AND TYPE = 'V' ORDER BY TABNAME";
        var p = cmd.CreateParameter();
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            views.Add(new TableOrView(reader.GetString(0).Trim(), targetSchema, Config.DefaultDatabase, "view"));
        }
        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        var pks = await GetPrimaryKeyColumnNames(table, targetSchema, ct);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT COLNAME, TYPENAME, NULLS, DEFAULT, LENGTH
            FROM SYSCAT.COLUMNS
            WHERE TABSCHEMA = ? AND TABNAME = ?
            ORDER BY COLNO";

        var pSchema = cmd.CreateParameter();
        pSchema.Value = targetSchema;
        cmd.Parameters.Add(pSchema);

        var pTable = cmd.CreateParameter();
        pTable.Value = table.ToUpperInvariant();
        cmd.Parameters.Add(pTable);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader.GetString(0).Trim();
            var typeName = reader.GetString(1).Trim();
            var nulls = reader.GetString(2).Trim();
            var defVal = reader.IsDBNull(3) ? null : reader.GetString(3);
            var len = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));

            columns.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = typeName,
                Nullable = string.Equals(nulls, "Y", StringComparison.OrdinalIgnoreCase),
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
                SELECT kc.COLNAME
                FROM SYSCAT.KEYCOLUSE kc
                JOIN SYSCAT.TABCONST tc
                  ON kc.CONSTNAME = tc.CONSTNAME
                 AND kc.TABSCHEMA = tc.TABSCHEMA
                 AND kc.TABNAME = tc.TABNAME
                WHERE tc.TYPE = 'P'
                  AND tc.TABSCHEMA = ?
                  AND tc.TABNAME = ?";

            var p1 = cmd.CreateParameter(); p1.Value = schema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                set.Add(reader.GetString(0).Trim());
            }
        }
        catch
        {
            // Ignore if catalog access is restricted
        }
        return set;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new List<TableIndex>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT INDNAME, UNIQUERULE FROM SYSCAT.INDEXES WHERE TABSCHEMA = ? AND TABNAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var indName = reader.GetString(0).Trim();
                var rule = reader.GetString(1).Trim();
                var isUnique = rule is "U" or "P";
                var isPk = rule == "P";

                indexes.Add(new TableIndex
                {
                    Name = indName,
                    IsUnique = isUnique,
                    IsPrimaryKey = isPk
                });
            }
        }
        catch
        {
            // Ignore if catalog access is restricted
        }

        return indexes;
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var keys = new List<TableKey>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = @"
                SELECT CONSTNAME, REFTABNAME
                FROM SYSCAT.REFERENCES
                WHERE TABSCHEMA = ? AND TABNAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                keys.Add(new TableKey
                {
                    ConstraintName = reader.GetString(0).Trim(),
                    ReferencedTable = reader.GetString(1).Trim()
                });
            }
        }
        catch
        {
            // Ignore if catalog access is restricted
        }

        return keys;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var triggers = new List<TableTrigger>();
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT TRIGNAME FROM SYSCAT.TRIGGERS WHERE TABSCHEMA = ? AND TABNAME = ?";
            var p1 = cmd.CreateParameter(); p1.Value = targetSchema; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.Value = table.ToUpperInvariant(); cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                triggers.Add(new TableTrigger { Name = reader.GetString(0).Trim() });
            }
        }
        catch
        {
            // Ignore if catalog access is restricted
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
            cmd.CommandText = "SELECT ROUTINENAME, ROUTINETYPE, ROUTINESCHEMA FROM SYSCAT.ROUTINES WHERE ROUTINESCHEMA NOT LIKE 'SYS%'";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                routines.Add(new Routine
                {
                    Name = reader.GetString(0).Trim(),
                    RoutineType = reader.GetString(1).Trim(),
                    Schema = reader.GetString(2).Trim()
                });
            }
        }
        catch
        {
            // Ignore if catalog access is restricted
        }

        return routines;
    }

    public override Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var targetSchema = string.IsNullOrWhiteSpace(schema)
            ? (Config.Username?.ToUpperInvariant() ?? "DB2ADMIN")
            : schema.ToUpperInvariant();

        return Task.FromResult($"-- DDL dla IBM DB2 {targetSchema}.{table.ToUpperInvariant()}\nCREATE TABLE \"{targetSchema}\".\"{table.ToUpperInvariant()}\" ();");
    }
}
