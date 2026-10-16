using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Common;

public abstract class DatabaseClientBase : IDatabaseClient
{
    public abstract ConnectionType ConnectionType { get; }
    public ConnectionConfig Config { get; }
    public abstract bool IsConnected { get; }

    protected DbConnection? CurrentConnection { get; set; }

    protected DatabaseClientBase(ConnectionConfig config)
    {
        Config = config;
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);

    public virtual async Task DisconnectAsync()
    {
        if (CurrentConnection != null)
        {
            if (CurrentConnection.State != ConnectionState.Closed)
            {
                await CurrentConnection.CloseAsync();
            }
            await CurrentConnection.DisposeAsync();
            CurrentConnection = null;
        }
    }

    public abstract Task<string> GetVersionAsync(CancellationToken ct = default);
    public abstract Task<SupportedFeatures> GetSupportedFeaturesAsync();

    public abstract Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default);
    public abstract Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default);
    public abstract Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default);

    public virtual async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        if (CurrentConnection == null || CurrentConnection.State != ConnectionState.Open)
            await ConnectAsync(ct);

        var sw = Stopwatch.StartNew();
        var result = new QueryResult { ExecutedSql = sql };

        try
        {
            using var cmd = CurrentConnection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;

            using var reader = await cmd.ExecuteReaderAsync(ct);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                result.Columns.Add(new QueryResultColumn
                {
                    Name = reader.GetName(i),
                    Type = reader.GetFieldType(i).Name,
                    Ordinal = i
                });
            }

            while (await reader.ReadAsync(ct))
            {
                var rawRow = new object[reader.FieldCount];
                reader.GetValues(rawRow);
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < rawRow.Length; i++)
                {
                    row[i] = rawRow[i] is DBNull ? null : rawRow[i];
                }
                result.Rows.Add(row);
            }

            result.AffectedRows = reader.RecordsAffected >= 0 ? reader.RecordsAffected : result.Rows.Count;
            result.IsSuccess = true;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.Elapsed;
        }

        return result;
    }

    public virtual async Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        string cleanSql = sql.Trim().TrimEnd(';');
        string explainSql = ConnectionType switch
        {
            ConnectionType.PostgreSql or ConnectionType.CockroachDb or ConnectionType.Redshift or ConnectionType.Supabase =>
                analyze ? $"EXPLAIN (ANALYZE, COSTS, VERBOSE, BUFFERS) {cleanSql}" : $"EXPLAIN (COSTS, VERBOSE) {cleanSql}",
            ConnectionType.Sqlite =>
                $"EXPLAIN QUERY PLAN {cleanSql}",
            ConnectionType.MySql or ConnectionType.MariaDB =>
                analyze ? $"EXPLAIN ANALYZE {cleanSql}" : $"EXPLAIN {cleanSql}",
            ConnectionType.DuckDb or ConnectionType.ClickHouse =>
                $"EXPLAIN {cleanSql}",
            ConnectionType.SqlServer =>
                $"SET SHOWPLAN_TEXT ON;\n{cleanSql};\nSET SHOWPLAN_TEXT OFF;",
            _ => $"EXPLAIN {cleanSql}"
        };

        return await ExecuteQueryAsync(explainSql, ct);
    }

    public virtual async IAsyncEnumerable<object?[]> StreamQueryAsync(string sql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (CurrentConnection == null || CurrentConnection.State != ConnectionState.Open)
            await ConnectAsync(ct);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var rawRow = new object[reader.FieldCount];
            reader.GetValues(rawRow);
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < rawRow.Length; i++)
            {
                row[i] = rawRow[i] is DBNull ? null : rawRow[i];
            }
            yield return row;
        }
    }

    public virtual async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        if (CurrentConnection == null || CurrentConnection.State != ConnectionState.Open)
            await ConnectAsync(ct);

        var sw = Stopwatch.StartNew();
        var columns = await ListTableColumnsAsync(options.Table, options.Schema, ct);
        var primaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();

        // Określ tryb identyfikacji wiersza (zwłaszcza gdy brak PK)
        var identifierMode = RowIdentifierMode.PrimaryKey;
        if (primaryKeys.Count == 0)
        {
            if (ConnectionType == ConnectionType.Sqlite)
                identifierMode = RowIdentifierMode.RowId;
            else if (ConnectionType == ConnectionType.PostgreSql)
                identifierMode = RowIdentifierMode.Ctid;
            else
            {
                var indexes = await ListTableIndexesAsync(options.Table, options.Schema, ct);
                var uniqueIndex = indexes.FirstOrDefault(i => i.IsUnique && i.Columns.Count > 0);
                identifierMode = uniqueIndex != null ? RowIdentifierMode.UniqueIndex : RowIdentifierMode.AllColumns;
            }
        }

        var fullTableName = BuildFullTableName(options.Table, options.Schema);

        // 1. Zliczanie rekordów (COUNT(*))
        var (filterSql, filterParams) = BuildWhereClause(options.Filters, options.RawWhereClause, columns);
        var countSql = $"SELECT COUNT(*) FROM {fullTableName} {filterSql}";

        long totalCount = 0;
        try
        {
            using var countCmd = CurrentConnection!.CreateCommand();
            countCmd.CommandText = countSql;
            AddParameters(countCmd, filterParams);
            var countObj = await countCmd.ExecuteScalarAsync(ct);
            if (countObj != null && countObj != DBNull.Value)
            {
                totalCount = Convert.ToInt64(countObj);
            }
        }
        catch
        {
            // Fallback jeśli count nie powiódł się
            totalCount = -1;
        }

        // 2. Pobieranie danych
        var selectBuilder = new StringBuilder();
        selectBuilder.Append("SELECT ");

        // Wstrzyknij identyfikator ukryty, jeśli brak PK
        if (identifierMode == RowIdentifierMode.RowId)
        {
            selectBuilder.Append("rowid AS _hornet_rowid, ");
        }
        else if (identifierMode == RowIdentifierMode.Ctid)
        {
            selectBuilder.Append("ctid::text AS _hornet_ctid, ");
        }

        if (options.SelectColumns != null && options.SelectColumns.Count > 0)
        {
            selectBuilder.Append(string.Join(", ", options.SelectColumns.Select(WrapIdentifier)));
        }
        else
        {
            selectBuilder.Append("*");
        }

        selectBuilder.Append($" FROM {fullTableName} {filterSql}");

        // ORDER BY
        if (options.OrderBy.Count > 0)
        {
            var orderParts = options.OrderBy.Select(o =>
                $"{WrapIdentifier(o.Field)} {(o.Direction == SortDirection.Descending ? "DESC" : "ASC")}");
            selectBuilder.Append(" ORDER BY ").Append(string.Join(", ", orderParts));
        }

        // Paging (LIMIT / OFFSET)
        selectBuilder.Append(BuildPagingClause(options.Limit, options.Offset));

        var rows = new List<Dictionary<string, object?>>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = selectBuilder.ToString();
        AddParameters(cmd, filterParams);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                rowDict[reader.GetName(i)] = val is DBNull ? null : val;
            }
            rows.Add(rowDict);
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
            IdentifierMode = identifierMode,
            Duration = sw.Elapsed
        };
    }

    public virtual async Task<TableUpdateResult> ApplyChangesAsync(TableChanges changes, CancellationToken ct = default)
    {
        if (CurrentConnection == null || CurrentConnection.State != ConnectionState.Open)
            await ConnectAsync(ct);

        if (Config.ReadOnlyMode)
            throw new InvalidOperationException("Połączenie jest w trybie tylko do odczytu (ReadOnlyMode). Modyfikacje są zablokowane.");

        var fullTableName = BuildFullTableName(changes.Table, changes.Schema);
        var columns = await ListTableColumnsAsync(changes.Table, changes.Schema, ct);
        var primaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();

        using var tx = await CurrentConnection!.BeginTransactionAsync(ct);
        int affected = 0;

        try
        {
            // 1. INSERTS
            foreach (var insert in changes.Inserts)
            {
                if (insert.Values.Count == 0) continue;

                var colNames = insert.Values.Keys.ToList();
                var paramNames = colNames.Select((_, i) => $"@ins_{i}").ToList();

                var sql = $"INSERT INTO {fullTableName} ({string.Join(", ", colNames.Select(WrapIdentifier))}) VALUES ({string.Join(", ", paramNames)})";
                using var cmd = CurrentConnection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;

                for (int i = 0; i < colNames.Count; i++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = paramNames[i];
                    p.Value = insert.Values[colNames[i]] ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }

                affected += await cmd.ExecuteNonQueryAsync(ct);
            }

            // 2. UPDATES
            foreach (var update in changes.Updates)
            {
                if (update.UpdatedValues.Count == 0) continue;

                var setParts = new List<string>();
                var updateCmd = CurrentConnection.CreateCommand();
                updateCmd.Transaction = tx;

                int pIndex = 0;
                foreach (var (col, val) in update.UpdatedValues)
                {
                    var pName = $"@set_{pIndex++}";
                    setParts.Add($"{WrapIdentifier(col)} = {pName}");
                    var p = updateCmd.CreateParameter();
                    p.ParameterName = pName;
                    p.Value = val ?? DBNull.Value;
                    updateCmd.Parameters.Add(p);
                }

                // Budowanie klauzuli WHERE identyfikującej wiersz
                var whereClause = BuildRowWhereClause(updateCmd, update.OriginalRow, primaryKeys, update.RowId, update.Ctid, changes.IdentifierMode);
                updateCmd.CommandText = $"UPDATE {fullTableName} SET {string.Join(", ", setParts)} WHERE {whereClause}";

                affected += await updateCmd.ExecuteNonQueryAsync(ct);
            }

            // 3. DELETES
            foreach (var delete in changes.Deletes)
            {
                var delCmd = CurrentConnection.CreateCommand();
                delCmd.Transaction = tx;

                var whereClause = BuildRowWhereClause(delCmd, delete.OriginalRow, primaryKeys, delete.RowId, delete.Ctid, changes.IdentifierMode);
                delCmd.CommandText = $"DELETE FROM {fullTableName} WHERE {whereClause}";

                affected += await delCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new TableUpdateResult { Success = true, AffectedRows = affected };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return new TableUpdateResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public virtual async Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var sql = $"TRUNCATE TABLE {BuildFullTableName(table, schema)}";
        await ExecuteQueryAsync(sql, ct);
    }

    public virtual async Task DropTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var sql = $"DROP TABLE {BuildFullTableName(table, schema)}";
        await ExecuteQueryAsync(sql, ct);
    }

    public abstract Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default);

    // Narzędzia budowania zapytań SQL
    public virtual string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    public virtual string EscapeLiteral(string value) => value.Replace("'", "''");

    public virtual string BuildFullTableName(string table, string? schema = null)
    {
        if (string.IsNullOrEmpty(schema))
            return WrapIdentifier(table);
        return $"{WrapIdentifier(schema)}.{WrapIdentifier(table)}";
    }

    public virtual string BuildPagingClause(int limit, int offset)
    {
        return $" LIMIT {limit} OFFSET {offset}";
    }

    protected virtual (string WhereSql, List<DbParameter> Parameters) BuildWhereClause(
        List<TableFilter> filters,
        string? rawWhereClause,
        IReadOnlyList<ExtendedTableColumn> columns)
    {
        // 1. Raw SQL filter
        if (!string.IsNullOrWhiteSpace(rawWhereClause))
        {
            var raw = rawWhereClause.Trim();
            if (raw.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[6..].Trim();
            }
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return ($"WHERE {raw}", []);
            }
        }

        if (filters == null || filters.Count == 0) return (string.Empty, []);

        var clauses = new List<string>();
        var parameters = new List<DbParameter>();

        int index = 0;
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
                continue;

            var pName = $"@flt_{index++}";
            var colName = WrapIdentifier(filter.Field);
            string clause;

            switch (filter.Operator)
            {
                case FilterOperator.Equals:
                    clause = $"{colName} = {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.NotEquals:
                    clause = $"{colName} != {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.Like:
                    clause = $"{colName} LIKE {pName}";
                    AddParam(parameters, pName, $"%{filter.Value}%");
                    break;
                case FilterOperator.NotLike:
                    clause = $"{colName} NOT LIKE {pName}";
                    AddParam(parameters, pName, $"%{filter.Value}%");
                    break;
                case FilterOperator.ILike:
                    if (ConnectionType == ConnectionType.PostgreSql)
                    {
                        clause = $"{colName} ILIKE {pName}";
                        AddParam(parameters, pName, $"%{filter.Value}%");
                    }
                    else
                    {
                        clause = $"LOWER({colName}) LIKE {pName}";
                        AddParam(parameters, pName, $"%{filter.Value?.ToString()?.ToLowerInvariant()}%");
                    }
                    break;
                case FilterOperator.NotILike:
                    if (ConnectionType == ConnectionType.PostgreSql)
                    {
                        clause = $"{colName} NOT ILIKE {pName}";
                        AddParam(parameters, pName, $"%{filter.Value}%");
                    }
                    else
                    {
                        clause = $"LOWER({colName}) NOT LIKE {pName}";
                        AddParam(parameters, pName, $"%{filter.Value?.ToString()?.ToLowerInvariant()}%");
                    }
                    break;
                case FilterOperator.LessThan:
                    clause = $"{colName} < {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.LessThanOrEqual:
                    clause = $"{colName} <= {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.GreaterThan:
                    clause = $"{colName} > {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.GreaterThanOrEqual:
                    clause = $"{colName} >= {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
                case FilterOperator.In:
                    var rawVal = filter.Value?.ToString() ?? "";
                    var items = rawVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (items.Length == 0)
                    {
                        clause = "1=1";
                    }
                    else
                    {
                        var inParamNames = new List<string>();
                        for (int i = 0; i < items.Length; i++)
                        {
                            var inParam = $"@flt_{index}_{i}";
                            inParamNames.Add(inParam);
                            AddParam(parameters, inParam, items[i]);
                        }
                        index++;
                        clause = $"{colName} IN ({string.Join(", ", inParamNames)})";
                    }
                    break;
                case FilterOperator.IsNull:
                    clause = $"{colName} IS NULL";
                    break;
                case FilterOperator.IsNotNull:
                    clause = $"{colName} IS NOT NULL";
                    break;
                default:
                    clause = $"{colName} = {pName}";
                    AddParam(parameters, pName, filter.Value);
                    break;
            }

            var prefix = clauses.Count > 0 ? (filter.LogicalOp == "OR" ? "OR " : "AND ") : "";
            clauses.Add(prefix + clause);
        }

        if (clauses.Count == 0)
            return (string.Empty, []);

        return ("WHERE " + string.Join(" ", clauses), parameters);
    }

    protected virtual string BuildRowWhereClause(
        DbCommand cmd,
        Dictionary<string, object?> originalRow,
        List<string> primaryKeys,
        long? rowId,
        string? ctid,
        RowIdentifierMode mode)
    {
        // 1. Jeśli przekazano RowId (SQLite)
        if (rowId.HasValue && mode == RowIdentifierMode.RowId)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@rowid";
            p.Value = rowId.Value;
            cmd.Parameters.Add(p);
            return "rowid = @rowid";
        }

        // 2. Jeśli przekazano Ctid (PostgreSQL)
        if (!string.IsNullOrEmpty(ctid) && mode == RowIdentifierMode.Ctid)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@ctid";
            p.Value = ctid;
            cmd.Parameters.Add(p);
            return "ctid = @ctid::tid";
        }

        // 3. Jeśli są jawne klucze główne
        if (primaryKeys.Count > 0 && mode == RowIdentifierMode.PrimaryKey)
        {
            var pkClauses = new List<string>();
            int idx = 0;
            foreach (var pk in primaryKeys)
            {
                var pName = $"@pk_{idx++}";
                var p = cmd.CreateParameter();
                p.ParameterName = pName;
                p.Value = originalRow.TryGetValue(pk, out var v) ? v ?? DBNull.Value : DBNull.Value;
                cmd.Parameters.Add(p);
                pkClauses.Add($"{WrapIdentifier(pk)} = {pName}");
            }
            return string.Join(" AND ", pkClauses);
        }

        // 4. Fallback dla tabel bez PK: All-Columns Matching (Optimistic Concurrency)
        var allClauses = new List<string>();
        int colIdx = 0;
        foreach (var (col, val) in originalRow)
        {
            if (col.StartsWith("_hornet_")) continue; // ignoruj wewnętrzne kolumny pomocnicze

            var pName = $"@orig_{colIdx++}";
            var p = cmd.CreateParameter();
            p.ParameterName = pName;
            p.Value = val ?? DBNull.Value;
            cmd.Parameters.Add(p);

            if (val == null)
            {
                allClauses.Add($"{WrapIdentifier(col)} IS NULL");
            }
            else
            {
                allClauses.Add($"{WrapIdentifier(col)} = {pName}");
            }
        }

        return string.Join(" AND ", allClauses);
    }

    private static void AddParam(List<DbParameter> list, string name, object? value)
    {
        var p = new Microsoft.Data.Sqlite.SqliteParameter(name, value ?? DBNull.Value);
        list.Add(p);
    }

    private static void AddParameters(DbCommand cmd, List<DbParameter> parameters)
    {
        foreach (var p in parameters)
        {
            var cp = cmd.CreateParameter();
            cp.ParameterName = p.ParameterName;
            cp.Value = p.Value;
            cmd.Parameters.Add(cp);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
