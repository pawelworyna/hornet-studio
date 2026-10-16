using System.Data;
using Microsoft.Data.SqlClient;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class SqlServerDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.SqlServer;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public SqlServerDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"[{name.Replace("]", "]]")}]";

    public override string BuildPagingClause(int limit, int offset)
    {
        // SQL Server wymaga OFFSET ... FETCH NEXT ... i wymaga klauzuli ORDER BY
        return $" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Config.Port.HasValue && Config.Port > 0 ? $"{Config.Host},{Config.Port}" : Config.Host,
            InitialCatalog = Config.DefaultDatabase ?? "master",
            ConnectTimeout = 15,
            TrustServerCertificate = !Config.Ssl.RejectUnauthorized,
            Encrypt = Config.Ssl.Enabled ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional
        };

        if (string.IsNullOrWhiteSpace(Config.Username))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = Config.Username;
            builder.Password = Config.Password ?? "";
        }

        var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT @@VERSION";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "Microsoft SQL Server";
    }

    public override Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = true,
            Comments = false,
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

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME NOT IN ('guest', 'INFORMATION_SCHEMA', 'sys') ORDER BY SCHEMA_NAME";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
        var p = cmd.CreateParameter(); p.ParameterName = "@schema"; p.Value = targetSchema; cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), targetSchema, CurrentConnection!.Database, "table"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'VIEW' ORDER BY TABLE_NAME";
        var p = cmd.CreateParameter(); p.ParameterName = "@schema"; p.Value = targetSchema; cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), targetSchema, CurrentConnection!.Database, "view"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var list = new List<ExtendedTableColumn>();

        var pks = await GetPrimaryKeyColumnNames(table, targetSchema, ct);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader["COLUMN_NAME"].ToString() ?? "";
            var isPk = pks.Contains(colName);
            list.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = reader["DATA_TYPE"]?.ToString() ?? "nvarchar",
                Nullable = reader["IS_NULLABLE"]?.ToString() == "YES",
                DefaultValue = reader["COLUMN_DEFAULT"] is DBNull ? null : reader["COLUMN_DEFAULT"]?.ToString(),
                IsPrimaryKey = isPk,
                MaxLength = reader["CHARACTER_MAXIMUM_LENGTH"] is int m ? m : null
            });
        }

        return list;
    }

    private async Task<HashSet<string>> GetPrimaryKeyColumnNames(string table, string schema, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
              AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = schema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            set.Add(reader.GetString(0));
        }
        return set;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var dict = new Dictionary<string, TableIndex>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT i.name AS IndexName, i.is_unique, i.is_primary_key, c.name AS ColumnName, i.type_desc
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY ic.key_ordinal";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var idxName = reader["IndexName"].ToString() ?? "";
            var isUnique = Convert.ToBoolean(reader["is_unique"]);
            var isPk = Convert.ToBoolean(reader["is_primary_key"]);
            var col = reader["ColumnName"].ToString() ?? "";
            var type = reader["type_desc"]?.ToString();

            if (!dict.TryGetValue(idxName, out var idx))
            {
                idx = new TableIndex
                {
                    Name = idxName,
                    IsUnique = isUnique,
                    IsPrimaryKey = isPk,
                    IndexType = type
                };
                dict[idxName] = idx;
            }
            idx.Columns.Add(col);
        }

        return dict.Values.ToList();
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var list = new List<TableKey>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                fk.name AS ConstraintName,
                c1.name AS ColumnName,
                OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
                c2.name AS ReferencedColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns c1 ON fkc.parent_object_id = c1.object_id AND fkc.parent_column_id = c1.column_id
            JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
            JOIN sys.tables t ON fk.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableKey
            {
                ConstraintName = reader["ConstraintName"].ToString() ?? "",
                ColumnName = reader["ColumnName"].ToString() ?? "",
                ReferencedTable = reader["ReferencedTable"].ToString() ?? "",
                ReferencedColumn = reader["ReferencedColumn"].ToString() ?? ""
            });
        }

        return list;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "dbo";
        var list = new List<TableTrigger>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT tr.name AS TriggerName
            FROM sys.triggers tr
            JOIN sys.tables t ON tr.parent_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableTrigger
            {
                Name = reader["TriggerName"].ToString() ?? ""
            });
        }
        return list;
    }

    public override async Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<Routine>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT ROUTINE_NAME, ROUTINE_TYPE, ROUTINE_SCHEMA
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_SCHEMA NOT IN ('guest', 'sys', 'INFORMATION_SCHEMA')
            ORDER BY ROUTINE_NAME";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Routine
            {
                Name = reader["ROUTINE_NAME"].ToString() ?? "",
                RoutineType = reader["ROUTINE_TYPE"].ToString() ?? "PROCEDURE",
                Schema = reader["ROUTINE_SCHEMA"]?.ToString()
            });
        }
        return list;
    }

    public override Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var targetSchema = schema ?? "dbo";
        return Task.FromResult($"-- DDL dla SQL Server [{targetSchema}].[{table}]\n-- Użyj narzędzia eksportu HornetStudio do wygenerowania pełnego skryptu.");
    }

    public override async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        // SQL Server wymaga klauzuli ORDER BY przy używaniu OFFSET ... FETCH NEXT
        if (options.OrderBy.Count == 0)
        {
            var columns = await ListTableColumnsAsync(options.Table, options.Schema, ct);
            var pk = columns.FirstOrDefault(c => c.IsPrimaryKey)?.ColumnName ?? columns.FirstOrDefault()?.ColumnName;
            if (!string.IsNullOrEmpty(pk))
            {
                options.OrderBy.Add(new OrderBy { Field = pk, Direction = SortDirection.Ascending });
            }
        }

        return await base.SelectTopAsync(options, ct);
    }
}
