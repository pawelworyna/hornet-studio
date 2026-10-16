using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class FirebirdDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.Firebird;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public FirebirdDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public override string BuildPagingClause(int limit, int offset)
    {
        if (offset > 0)
        {
            return $" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
        }
        return $" FETCH FIRST {limit} ROWS ONLY";
    }

    public string BuildConnectionString()
    {
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            return Config.ConnectionString;
        }

        var dbName = !string.IsNullOrWhiteSpace(Config.DefaultDatabase)
            ? Config.DefaultDatabase
            : (!string.IsNullOrWhiteSpace(Config.DatabasePath) ? Config.DatabasePath : "employee.fdb");

        var builder = new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host,
            Port = Config.GetEffectivePort() > 0 ? Config.GetEffectivePort() : 3050,
            Database = dbName,
            UserID = string.IsNullOrWhiteSpace(Config.Username) ? "SYSDBA" : Config.Username,
            Password = Config.Password ?? "masterkey",
            Charset = "UTF8",
            ConnectionTimeout = 15,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var connStr = BuildConnectionString();
        var conn = new FbConnection(connStr);
        try
        {
            await conn.OpenAsync(ct);
            CurrentConnection = conn;
        }
        catch (Exception ex)
        {
            conn.Dispose();
            throw new InvalidOperationException(
                $"Failed to connect to Firebird database ({Config.Host}:{Config.GetEffectivePort()} / {Config.DefaultDatabase}): {ex.Message}. " +
                "Please verify that Firebird server is running and the database file/alias exists.", ex);
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";
            var res = await cmd.ExecuteScalarAsync(ct);
            if (res != null && !string.IsNullOrWhiteSpace(res.ToString()))
            {
                return $"Firebird {res}";
            }
        }
        catch
        {
            // Fallback
        }
        return "Firebird SQL Server";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = true,
            Comments = false,
            Properties = true,
            Partitions = false,
            Backups = true,
            Restore = true,
            Schemas = false,
            Truncate = true,
            ExplainPlan = true,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(RDB$RELATION_NAME) AS TABLE_NAME
            FROM RDB$RELATIONS
            WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)
              AND RDB$VIEW_SOURCE IS NULL
            ORDER BY RDB$RELATION_NAME";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            tables.Add(new TableOrView(name, schema ?? "DEFAULT", null, "table"));
        }

        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(RDB$RELATION_NAME) AS VIEW_NAME
            FROM RDB$RELATIONS
            WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)
              AND RDB$VIEW_SOURCE IS NOT NULL
            ORDER BY RDB$RELATION_NAME";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            views.Add(new TableOrView(name, schema ?? "DEFAULT", null, "view"));
        }

        return views;
    }

    public override Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        var dbName = !string.IsNullOrWhiteSpace(Config.DefaultDatabase)
            ? Config.DefaultDatabase
            : (!string.IsNullOrWhiteSpace(Config.DatabasePath) ? Config.DatabasePath : "employee.fdb");
        var name = System.IO.Path.GetFileName(dbName);
        return Task.FromResult<IReadOnlyList<string>>([string.IsNullOrWhiteSpace(name) ? "employee.fdb" : name]);
    }

    public override Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(new[] { "DEFAULT" });
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();

        // 1. Fetch Primary Keys
        var primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var pkCmd = CurrentConnection!.CreateCommand();
            pkCmd.CommandText = @"
                SELECT TRIM(iseg.RDB$FIELD_NAME) AS PK_FIELD
                FROM RDB$RELATION_CONSTRAINTS rc
                JOIN RDB$INDEX_SEGMENTS iseg ON rc.RDB$INDEX_NAME = iseg.RDB$INDEX_NAME
                WHERE UPPER(rc.RDB$RELATION_NAME) = UPPER(@tbl)
                  AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
                ORDER BY iseg.RDB$FIELD_POSITION";

            var prm = pkCmd.CreateParameter();
            prm.ParameterName = "@tbl";
            prm.Value = table;
            pkCmd.Parameters.Add(prm);

            using var pkReader = await pkCmd.ExecuteReaderAsync(ct);
            while (await pkReader.ReadAsync(ct))
            {
                primaryKeys.Add(pkReader.GetString(0));
            }
        }
        catch { }

        // 2. Fetch Columns
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
              TRIM(rf.RDB$FIELD_NAME) AS COLUMN_NAME,
              CASE f.RDB$FIELD_TYPE
                WHEN 7 THEN 'SMALLINT'
                WHEN 8 THEN 'INTEGER'
                WHEN 9 THEN 'QUAD'
                WHEN 10 THEN 'FLOAT'
                WHEN 12 THEN 'DATE'
                WHEN 13 THEN 'TIME'
                WHEN 14 THEN 'CHAR'
                WHEN 16 THEN 'BIGINT'
                WHEN 27 THEN 'DOUBLE PRECISION'
                WHEN 35 THEN 'TIMESTAMP'
                WHEN 37 THEN 'VARCHAR'
                WHEN 40 THEN 'CSTRING'
                WHEN 261 THEN 'BLOB'
                ELSE 'VARCHAR'
              END AS DATA_TYPE,
              COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0) AS IS_NOT_NULL,
              rf.RDB$FIELD_POSITION AS POSITION,
              TRIM(rf.RDB$DEFAULT_SOURCE) AS DEFAULT_VAL
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
            WHERE UPPER(rf.RDB$RELATION_NAME) = UPPER(@tbl)
            ORDER BY rf.RDB$FIELD_POSITION";

        var tableParam = cmd.CreateParameter();
        tableParam.ParameterName = "@tbl";
        tableParam.Value = table;
        cmd.Parameters.Add(tableParam);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNotNull = Convert.ToInt32(reader[2]) == 1;
            var defaultVal = reader.IsDBNull(4) ? null : reader.GetString(4);

            columns.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = dataType,
                Nullable = !isNotNull,
                IsPrimaryKey = primaryKeys.Contains(colName),
                DefaultValue = defaultVal
            });
        }

        return columns;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var indexes = new Dictionary<string, TableIndex>(StringComparer.OrdinalIgnoreCase);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
              TRIM(i.RDB$INDEX_NAME) AS INDEX_NAME,
              TRIM(s.RDB$FIELD_NAME) AS COLUMN_NAME,
              CASE WHEN i.RDB$UNIQUE_FLAG = 1 THEN 1 ELSE 0 END AS IS_UNIQUE,
              s.RDB$FIELD_POSITION AS SEQ_POS
            FROM RDB$INDICES i
            JOIN RDB$INDEX_SEGMENTS s ON i.RDB$INDEX_NAME = s.RDB$INDEX_NAME
            WHERE UPPER(i.RDB$RELATION_NAME) = UPPER(@tbl)
              AND (i.RDB$SYSTEM_FLAG = 0 OR i.RDB$SYSTEM_FLAG IS NULL)
            ORDER BY i.RDB$INDEX_NAME, s.RDB$FIELD_POSITION";

        var p = cmd.CreateParameter();
        p.ParameterName = "@tbl";
        p.Value = table;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var idxName = reader.GetString(0);
            var colName = reader.GetString(1);
            var isUnique = Convert.ToInt32(reader[2]) == 1;

            if (!indexes.TryGetValue(idxName, out var idx))
            {
                idx = new TableIndex
                {
                    Name = idxName,
                    IsUnique = isUnique,
                    Columns = new List<string>()
                };
                indexes[idxName] = idx;
            }
            ((List<string>)idx.Columns).Add(colName);
        }

        return indexes.Values.ToList();
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var keys = new List<TableKey>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
              TRIM(rc.RDB$CONSTRAINT_NAME) AS CONSTRAINT_NAME,
              TRIM(rc.RDB$CONSTRAINT_TYPE) AS CONSTRAINT_TYPE,
              TRIM(iseg.RDB$FIELD_NAME) AS COLUMN_NAME,
              TRIM(refc.RDB$CONST_NAME_UQ) AS REFERENCED_CONSTRAINT
            FROM RDB$RELATION_CONSTRAINTS rc
            LEFT JOIN RDB$INDEX_SEGMENTS iseg ON rc.RDB$INDEX_NAME = iseg.RDB$INDEX_NAME
            LEFT JOIN RDB$REF_CONSTRAINTS refc ON rc.RDB$CONSTRAINT_NAME = refc.RDB$CONSTRAINT_NAME
            WHERE UPPER(rc.RDB$RELATION_NAME) = UPPER(@tbl)
            ORDER BY rc.RDB$CONSTRAINT_NAME, iseg.RDB$FIELD_POSITION";

        var p = cmd.CreateParameter();
        p.ParameterName = "@tbl";
        p.Value = table;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            keys.Add(new TableKey
            {
                ConstraintName = reader.GetString(0),
                ColumnName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ReferencedTable = reader.IsDBNull(3) ? "" : reader.GetString(3)
            });
        }

        return keys;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var triggers = new List<TableTrigger>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
              TRIM(RDB$TRIGGER_NAME) AS TRIGGER_NAME,
              CASE RDB$TRIGGER_TYPE
                WHEN 1 THEN 'BEFORE INSERT'
                WHEN 2 THEN 'AFTER INSERT'
                WHEN 3 THEN 'BEFORE UPDATE'
                WHEN 4 THEN 'AFTER UPDATE'
                WHEN 5 THEN 'BEFORE DELETE'
                WHEN 6 THEN 'AFTER DELETE'
                ELSE 'TRIGGER'
              END AS EVENT_NAME,
              TRIM(RDB$TRIGGER_SOURCE) AS SOURCE
            FROM RDB$TRIGGERS
            WHERE UPPER(RDB$RELATION_NAME) = UPPER(@tbl)
              AND (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)";

        var p = cmd.CreateParameter();
        p.ParameterName = "@tbl";
        p.Value = table;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var evtName = reader.GetString(1);
            triggers.Add(new TableTrigger
            {
                Name = reader.GetString(0),
                Event = evtName,
                Timing = evtName.StartsWith("BEFORE", StringComparison.OrdinalIgnoreCase) ? "BEFORE" : "AFTER",
                Definition = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return triggers;
    }

    public override async Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var routines = new List<Routine>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(RDB$PROCEDURE_NAME) AS ROUTINE_NAME, 'PROCEDURE' AS ROUTINE_TYPE
            FROM RDB$PROCEDURES
            WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)
            UNION ALL
            SELECT TRIM(RDB$FUNCTION_NAME) AS ROUTINE_NAME, 'FUNCTION' AS ROUTINE_TYPE
            FROM RDB$FUNCTIONS
            WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL)";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            routines.Add(new Routine
            {
                Name = reader.GetString(0),
                RoutineType = reader.GetString(1)
            });
        }

        return routines;
    }

    public override Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        return Task.FromResult(new QueryResult
        {
            IsSuccess = false,
            ErrorMessage = "Execution plans in Firebird are generated upon statement execution.",
            ExecutedSql = sql
        });
    }

    public override async Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var cols = await ListTableColumnsAsync(table, schema, ct);
        var colDefs = new List<string>();
        var pks = new List<string>();

        foreach (var c in cols)
        {
            var nullability = c.Nullable ? "" : " NOT NULL";
            var dflt = c.DefaultValue != null ? $" DEFAULT {c.DefaultValue}" : "";
            colDefs.Add($"  {WrapIdentifier(c.ColumnName)} {c.DataType}{nullability}{dflt}");
            if (c.IsPrimaryKey) pks.Add(WrapIdentifier(c.ColumnName));
        }

        if (pks.Count > 0)
        {
            colDefs.Add($"  CONSTRAINT PK_{table.ToUpperInvariant()} PRIMARY KEY ({string.Join(", ", pks)})");
        }

        return $"CREATE TABLE {WrapIdentifier(table)} (\n{string.Join(",\n", colDefs)}\n);";
    }
}
