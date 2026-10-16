using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class MsAccessDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.MsAccess;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public MsAccessDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"[{name.Replace("]", "]]")}]";

    public override string BuildPagingClause(int limit, int offset)
    {
        // MS Access does not support LIMIT/OFFSET; handled in SelectTopAsync via TOP clause
        return string.Empty;
    }

    public string GetFilePath()
    {
        return !string.IsNullOrWhiteSpace(Config.DatabasePath)
            ? Config.DatabasePath
            : (!string.IsNullOrWhiteSpace(Config.Host) ? Config.Host : "Database.accdb");
    }

    public string BuildConnectionString()
    {
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            return Config.ConnectionString;
        }

        var path = GetFilePath();
        return $"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};DBQ={path};";
    }

    public string BuildOleDbConnectionString()
    {
        var path = GetFilePath();
        var pwdPart = string.IsNullOrWhiteSpace(Config.Password) ? "" : $"Jet OLEDB:Database Password={Config.Password};";
        return $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={path};Persist Security Info=False;{pwdPart}";
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var path = GetFilePath();
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
                $"Failed to connect to Microsoft Access database at '{path}': {ex.Message}. " +
                "Please verify that Microsoft Access Database Engine (ODBC/ACE Driver) is installed and the database file exists.", ex);
        }
    }

    public override Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var path = GetFilePath();
        var ext = Path.GetExtension(path)?.ToUpperInvariant() ?? ".ACCDB";
        return Task.FromResult($"Microsoft Access Database Engine ({ext})");
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
            Truncate = false,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public override Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        return Task.FromResult(new QueryResult
        {
            IsSuccess = false,
            ErrorMessage = "EXPLAIN is not supported by Microsoft Access JET/ACE engine."
        });
    }

    public override Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        var path = GetFilePath();
        var name = Path.GetFileName(path);
        return Task.FromResult<IReadOnlyList<string>>([string.IsNullOrWhiteSpace(name) ? "Database.accdb" : name]);
    }

    public override Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var tables = new List<TableOrView>();

        try
        {
            var schemaTable = CurrentConnection!.GetSchema("Tables");
            foreach (DataRow row in schemaTable.Rows)
            {
                var tableName = row["TABLE_NAME"]?.ToString() ?? "";
                var tableType = row["TABLE_TYPE"]?.ToString() ?? "";

                if (string.Equals(tableType, "TABLE", StringComparison.OrdinalIgnoreCase) &&
                    !tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) &&
                    !tableName.StartsWith("~", StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(new TableOrView(tableName, "default", GetFilePath(), "table"));
                }
            }
        }
        catch
        {
            // Fallback: try querying MSysObjects if allowed
            try
            {
                using var cmd = CurrentConnection!.CreateCommand();
                cmd.CommandText = "SELECT Name FROM MSysObjects WHERE Type=1 AND Flags=0 ORDER BY Name";
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    tables.Add(new TableOrView(reader.GetString(0), "default", GetFilePath(), "table"));
                }
            }
            catch
            {
                // Catalog restricted
            }
        }

        return tables;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var views = new List<TableOrView>();

        try
        {
            var schemaTable = CurrentConnection!.GetSchema("Tables");
            foreach (DataRow row in schemaTable.Rows)
            {
                var tableName = row["TABLE_NAME"]?.ToString() ?? "";
                var tableType = row["TABLE_TYPE"]?.ToString() ?? "";

                if ((string.Equals(tableType, "VIEW", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tableType, "QUERY", StringComparison.OrdinalIgnoreCase)) &&
                    !tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) &&
                    !tableName.StartsWith("~", StringComparison.OrdinalIgnoreCase))
                {
                    views.Add(new TableOrView(tableName, "default", GetFilePath(), "view"));
                }
            }
        }
        catch
        {
            // Ignore if schema reading failed
        }

        return views;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var columns = new List<ExtendedTableColumn>();

        var pks = await GetPrimaryKeyColumnNames(table, ct);

        try
        {
            var colsTable = CurrentConnection!.GetSchema("Columns", [null, null, table]);
            foreach (DataRow row in colsTable.Rows)
            {
                var colName = row["COLUMN_NAME"]?.ToString() ?? "";
                var dataTypeName = row.Table.Columns.Contains("DATA_TYPE") ? row["DATA_TYPE"]?.ToString() ?? "text" : "text";
                var isNullable = !row.Table.Columns.Contains("IS_NULLABLE") ||
                                 string.Equals(row["IS_NULLABLE"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(row["IS_NULLABLE"]?.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase);

                columns.Add(new ExtendedTableColumn
                {
                    ColumnName = colName,
                    DataType = dataTypeName,
                    Nullable = isNullable,
                    IsPrimaryKey = pks.Contains(colName)
                });
            }
        }
        catch
        {
            // Fallback: execute SELECT TOP 1 * FROM [table] and reflect from DataReader
            try
            {
                using var cmd = CurrentConnection!.CreateCommand();
                cmd.CommandText = $"SELECT TOP 1 * FROM {WrapIdentifier(table)}";
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
                var schemaDt = reader.GetSchemaTable();
                if (schemaDt != null)
                {
                    foreach (DataRow r in schemaDt.Rows)
                    {
                        var colName = r["ColumnName"]?.ToString() ?? "";
                        var dataTypeName = r["DataType"]?.ToString() ?? "String";
                        var isNullable = r.Table.Columns.Contains("AllowDBNull") && Convert.ToBoolean(r["AllowDBNull"]);
                        var isKey = r.Table.Columns.Contains("IsKey") && Convert.ToBoolean(r["IsKey"]);

                        columns.Add(new ExtendedTableColumn
                        {
                            ColumnName = colName,
                            DataType = dataTypeName,
                            Nullable = isNullable,
                            IsPrimaryKey = isKey || pks.Contains(colName)
                        });
                    }
                }
            }
            catch
            {
                // Schema fallback
            }
        }

        return columns;
    }

    private Task<HashSet<string>> GetPrimaryKeyColumnNames(string table, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var indexesTable = CurrentConnection!.GetSchema("Indexes", [null, null, null, null, table]);
            foreach (DataRow row in indexesTable.Rows)
            {
                var isPrimary = row.Table.Columns.Contains("PRIMARY_KEY") && Convert.ToBoolean(row["PRIMARY_KEY"]);
                if (isPrimary && row.Table.Columns.Contains("COLUMN_NAME"))
                {
                    var col = row["COLUMN_NAME"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(col))
                    {
                        set.Add(col);
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
        return Task.FromResult(set);
    }

    public override Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var indexes = new List<TableIndex>();
        try
        {
            if (IsConnected)
            {
                var indexesTable = CurrentConnection!.GetSchema("Indexes", [null, null, null, null, table]);
                var dict = new Dictionary<string, TableIndex>(StringComparer.OrdinalIgnoreCase);

                foreach (DataRow row in indexesTable.Rows)
                {
                    var idxName = row["INDEX_NAME"]?.ToString() ?? "";
                    var isUnique = row.Table.Columns.Contains("UNIQUE") && Convert.ToBoolean(row["UNIQUE"]);
                    var isPrimary = row.Table.Columns.Contains("PRIMARY_KEY") && Convert.ToBoolean(row["PRIMARY_KEY"]);
                    var colName = row.Table.Columns.Contains("COLUMN_NAME") ? row["COLUMN_NAME"]?.ToString() : null;

                    if (!dict.TryGetValue(idxName, out var idx))
                    {
                        idx = new TableIndex
                        {
                            Name = idxName,
                            IsUnique = isUnique || isPrimary,
                            IsPrimaryKey = isPrimary
                        };
                        dict[idxName] = idx;
                    }
                    if (!string.IsNullOrEmpty(colName))
                    {
                        idx.Columns.Add(colName);
                    }
                }

                indexes.AddRange(dict.Values);
            }
        }
        catch
        {
            // Ignore
        }
        return Task.FromResult<IReadOnlyList<TableIndex>>(indexes);
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

    public override async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        if (CurrentConnection == null || CurrentConnection.State != ConnectionState.Open)
            await ConnectAsync(ct);

        var sw = Stopwatch.StartNew();
        var columns = await ListTableColumnsAsync(options.Table, options.Schema, ct);
        var primaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();

        var fullTableName = WrapIdentifier(options.Table);
        var (filterSql, filterParams) = BuildWhereClause(options.Filters, options.RawWhereClause, columns);

        // 1. Zliczanie COUNT(*)
        long totalCount = 0;
        try
        {
            using var countCmd = CurrentConnection!.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {fullTableName} {filterSql}";
            foreach (var p in filterParams)
            {
                var cp = countCmd.CreateParameter();
                cp.ParameterName = p.ParameterName;
                cp.Value = p.Value;
                countCmd.Parameters.Add(cp);
            }
            var countObj = await countCmd.ExecuteScalarAsync(ct);
            if (countObj != null && countObj != DBNull.Value)
            {
                totalCount = Convert.ToInt64(countObj);
            }
        }
        catch
        {
            totalCount = -1;
        }

        // 2. Pobieranie danych z TOP (offset + limit)
        var totalRowsToFetch = options.Offset + options.Limit;
        var selectBuilder = new StringBuilder();
        selectBuilder.Append($"SELECT TOP {totalRowsToFetch} ");

        if (options.SelectColumns != null && options.SelectColumns.Count > 0)
        {
            selectBuilder.Append(string.Join(", ", options.SelectColumns.Select(WrapIdentifier)));
        }
        else
        {
            selectBuilder.Append("*");
        }

        selectBuilder.Append($" FROM {fullTableName} {filterSql}");

        if (options.OrderBy.Count > 0)
        {
            var orderParts = options.OrderBy.Select(o =>
                $"{WrapIdentifier(o.Field)} {(o.Direction == SortDirection.Descending ? "DESC" : "ASC")}");
            selectBuilder.Append(" ORDER BY ").Append(string.Join(", ", orderParts));
        }

        var rows = new List<Dictionary<string, object?>>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = selectBuilder.ToString();
        foreach (var p in filterParams)
        {
            var cp = cmd.CreateParameter();
            cp.ParameterName = p.ParameterName;
            cp.Value = p.Value;
            cmd.Parameters.Add(cp);
        }

        using var reader = await cmd.ExecuteReaderAsync(ct);
        int skipped = 0;
        while (await reader.ReadAsync(ct))
        {
            if (skipped < options.Offset)
            {
                skipped++;
                continue;
            }

            var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                rowDict[reader.GetName(i)] = val is DBNull ? null : val;
            }
            rows.Add(rowDict);

            if (rows.Count >= options.Limit)
                break;
        }

        sw.Stop();

        return new TableResult
        {
            Columns = columns.ToList(),
            Rows = rows,
            TotalCount = totalCount,
            Offset = options.Offset,
            Limit = options.Limit,
            PrimaryKeys = primaryKeys,
            IdentifierMode = primaryKeys.Count > 0 ? RowIdentifierMode.PrimaryKey : RowIdentifierMode.AllColumns,
            Duration = sw.Elapsed
        };
    }

    public override Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult($"CREATE TABLE [{table}] (\n    [Id] AUTOINCREMENT PRIMARY KEY\n);");
    }
}
