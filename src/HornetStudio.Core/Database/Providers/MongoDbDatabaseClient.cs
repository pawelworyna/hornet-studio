using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using CoreSortDirection = HornetStudio.Core.Domain.Enums.SortDirection;

namespace HornetStudio.Core.Database.Providers;

public class MongoDbDatabaseClient : IDatabaseClient
{
    public ConnectionType ConnectionType => ConnectionType.MongoDb;
    public ConnectionConfig Config { get; }
    public bool IsConnected => _client != null;

    private IMongoClient? _client;
    private IMongoDatabase? _currentDatabase;

    public MongoDbDatabaseClient(ConnectionConfig config)
    {
        Config = config;
    }

    public string WrapIdentifier(string name) => name;
    public string EscapeLiteral(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_client != null) return;

        string connStr;
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            connStr = Config.ConnectionString;
        }
        else
        {
            var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
            var port = Config.GetEffectivePort();
            var user = Config.Username;
            var pass = Config.Password;
            var defaultDb = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "admin" : Config.DefaultDatabase;

            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
            {
                connStr = $"mongodb://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(pass)}@{host}:{port}/?authSource={defaultDb}";
            }
            else
            {
                connStr = $"mongodb://{host}:{port}";
            }
        }

        var settings = MongoClientSettings.FromConnectionString(connStr);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        _client = new MongoClient(settings);

        var targetDb = string.IsNullOrWhiteSpace(Config.DefaultDatabase) ? "admin" : Config.DefaultDatabase;
        _currentDatabase = _client.GetDatabase(targetDb);

        // Ping MongoDB server to verify connection
        await _currentDatabase.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    }

    public Task DisconnectAsync()
    {
        _client = null;
        _currentDatabase = null;
        return Task.CompletedTask;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        try
        {
            var buildInfo = await _currentDatabase!.RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: ct);
            if (buildInfo.Contains("version"))
            {
                return $"MongoDB {buildInfo["version"].AsString}";
            }
        }
        catch { }

        return "MongoDB";
    }

    public Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            CustomRoutines = false,
            Comments = false,
            Properties = false,
            Partitions = false,
            Backups = false,
            Restore = false,
            Schemas = false,
            Truncate = true,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        using var cursor = await _client!.ListDatabaseNamesAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            list.AddRange(cursor.Current);
        }
        return list.Count > 0 ? list : [Config.DefaultDatabase ?? "admin"];
    }

    public Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "admin" : schema;
        var db = _client!.GetDatabase(dbName);
        var collections = new List<TableOrView>();

        using var cursor = await db.ListCollectionNamesAsync(cancellationToken: ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var collName in cursor.Current)
            {
                if (collName.StartsWith("system.")) continue;
                collections.Add(new TableOrView(collName, dbName, null, "collection"));
            }
        }
        return collections;
    }

    public Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableOrView>>([]);
    }

    public async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "admin" : schema;
        var collection = _client!.GetDatabase(dbName).GetCollection<BsonDocument>(table);

        var cols = new Dictionary<string, ExtendedTableColumn>
        {
            ["_id"] = new ExtendedTableColumn
            {
                ColumnName = "_id",
                DataType = "ObjectId",
                Nullable = false,
                IsPrimaryKey = true
            }
        };

        var sampleDocs = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Limit(50)
            .ToListAsync(ct);

        foreach (var doc in sampleDocs)
        {
            foreach (var elem in doc.Elements)
            {
                if (cols.ContainsKey(elem.Name)) continue;
                cols[elem.Name] = new ExtendedTableColumn
                {
                    ColumnName = elem.Name,
                    DataType = elem.Value.BsonType.ToString(),
                    Nullable = true,
                    IsPrimaryKey = false
                };
            }
        }

        return cols.Values.ToList();
    }

    public async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "admin" : schema;
        var collection = _client!.GetDatabase(dbName).GetCollection<BsonDocument>(table);
        var indexes = new List<TableIndex>();

        try
        {
            using var cursor = await collection.Indexes.ListAsync(ct);
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var doc in cursor.Current)
                {
                    var name = doc.GetValue("name", "").AsString;
                    var isUnique = doc.GetValue("unique", false).AsBoolean;
                    var keyDoc = doc.GetValue("key", new BsonDocument()).AsBsonDocument;

                    var idx = new TableIndex
                    {
                        Name = name,
                        IsUnique = isUnique,
                        IsPrimaryKey = name == "_id_"
                    };
                    foreach (var k in keyDoc.Names)
                    {
                        idx.Columns.Add(k);
                    }
                    indexes.Add(idx);
                }
            }
        }
        catch { }

        return indexes;
    }

    public Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableKey>>([]);
    }

    public Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableTrigger>>([]);
    }

    public Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Routine>>([]);
    }

    public async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var sw = Stopwatch.StartNew();
        var dbName = string.IsNullOrWhiteSpace(options.Schema) ? Config.DefaultDatabase ?? "admin" : options.Schema;
        var collection = _client!.GetDatabase(dbName).GetCollection<BsonDocument>(options.Table);

        // Filter
        FilterDefinition<BsonDocument> filter = FilterDefinition<BsonDocument>.Empty;
        if (!string.IsNullOrWhiteSpace(options.RawWhereClause))
        {
            var rawWhere = options.RawWhereClause.Trim();
            if (rawWhere.StartsWith("{") && rawWhere.EndsWith("}"))
            {
                try
                {
                    filter = BsonDocument.Parse(rawWhere);
                }
                catch { }
            }
        }

        long totalCount = 0;
        try
        {
            totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
        }
        catch
        {
            totalCount = -1;
        }

        var query = collection.Find(filter);

        // Sorting
        if (options.OrderBy.Count > 0)
        {
            var sortBson = new BsonDocument();
            foreach (var ob in options.OrderBy)
            {
                sortBson[ob.Field] = ob.Direction == CoreSortDirection.Ascending ? 1 : -1;
            }
            query = query.Sort(sortBson);
        }
        else
        {
            query = query.Sort(new BsonDocument("_id", -1));
        }

        if (options.Offset > 0)
            query = query.Skip(options.Offset);

        query = query.Limit(options.Limit > 0 ? options.Limit : 50);

        var docs = await query.ToListAsync(ct);

        // Deduce columns from sampled collection metadata
        var columns = await ListTableColumnsAsync(options.Table, options.Schema, ct);
        var colMap = columns.ToDictionary(c => c.ColumnName, c => c);

        // Also check if any returned doc has extra fields
        foreach (var doc in docs)
        {
            foreach (var elem in doc.Elements)
            {
                if (!colMap.ContainsKey(elem.Name))
                {
                    var extraCol = new ExtendedTableColumn
                    {
                        ColumnName = elem.Name,
                        DataType = elem.Value.BsonType.ToString(),
                        Nullable = true
                    };
                    colMap[elem.Name] = extraCol;
                }
            }
        }

        var orderedCols = colMap.Values.ToList();

        var rows = new List<Dictionary<string, object?>>();
        foreach (var doc in docs)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in orderedCols)
            {
                if (doc.Contains(col.ColumnName))
                {
                    row[col.ColumnName] = FormatBsonValue(doc[col.ColumnName]);
                }
                else
                {
                    row[col.ColumnName] = null;
                }
            }
            rows.Add(row);
        }

        sw.Stop();
        return new TableResult
        {
            Columns = orderedCols,
            Rows = rows,
            TotalCount = totalCount,
            Offset = options.Offset,
            Limit = options.Limit,
            PrimaryKeys = ["_id"],
            IdentifierMode = RowIdentifierMode.PrimaryKey,
            Duration = sw.Elapsed
        };
    }

    public async Task<TableUpdateResult> ApplyChangesAsync(TableChanges changes, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(changes.Schema) ? Config.DefaultDatabase ?? "admin" : changes.Schema;
        var collection = _client!.GetDatabase(dbName).GetCollection<BsonDocument>(changes.Table);

        var res = new TableUpdateResult { Success = true };

        try
        {
            // Deletions
            foreach (var del in changes.Deletes)
            {
                if (del.OriginalRow.TryGetValue("_id", out var idVal) && idVal != null)
                {
                    var filter = BuildIdFilter(idVal);
                    await collection.DeleteOneAsync(filter, cancellationToken: ct);
                    res.AffectedRows++;
                }
            }

            // Updates
            foreach (var upd in changes.Updates)
            {
                if (upd.OriginalRow.TryGetValue("_id", out var idVal) && idVal != null)
                {
                    var filter = BuildIdFilter(idVal);
                    var updates = new List<UpdateDefinition<BsonDocument>>();
                    foreach (var (col, val) in upd.UpdatedValues)
                    {
                        if (col == "_id") continue;
                        var bsonVal = ConvertToBsonValue(val);
                        updates.Add(Builders<BsonDocument>.Update.Set(col, bsonVal));
                    }

                    if (updates.Count > 0)
                    {
                        var combined = Builders<BsonDocument>.Update.Combine(updates);
                        await collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
                        res.AffectedRows++;
                    }
                }
            }

            // Insertions
            foreach (var ins in changes.Inserts)
            {
                var doc = new BsonDocument();
                foreach (var (col, val) in ins.Values)
                {
                    if (col == "_id" && (val == null || string.IsNullOrWhiteSpace(val.ToString())))
                    {
                        doc["_id"] = ObjectId.GenerateNewId();
                    }
                    else
                    {
                        doc[col] = ConvertToBsonValue(val);
                    }
                }
                if (!doc.Contains("_id"))
                {
                    doc["_id"] = ObjectId.GenerateNewId();
                }

                await collection.InsertOneAsync(doc, cancellationToken: ct);
                res.AffectedRows++;
            }
        }
        catch (Exception ex)
        {
            res.Success = false;
            res.ErrorMessage = ex.Message;
        }

        return res;
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var sw = Stopwatch.StartNew();
        var result = new QueryResult { ExecutedSql = sql };

        try
        {
            var trimmed = sql.Trim();

            // 1. Check for SQL SELECT * FROM collection syntax
            var selectMatch = Regex.Match(trimmed, @"^SELECT\s+(.+?)\s+FROM\s+([`""\w]+)(?:\s+WHERE\s+(.+?))?(?:\s+LIMIT\s+(\d+))?$", RegexOptions.IgnoreCase);
            if (selectMatch.Success)
            {
                var collName = selectMatch.Groups[2].Value.Trim('`', '"');
                var limit = selectMatch.Groups[4].Success ? int.Parse(selectMatch.Groups[4].Value) : 100;
                var tableRes = await SelectTopAsync(new SelectTopOptions { Table = collName, Limit = limit }, ct);

                result.Columns = tableRes.Columns.Select((c, i) => new QueryResultColumn
                {
                    Name = c.ColumnName,
                    Type = c.DataType,
                    Ordinal = i
                }).ToList();

                foreach (var rowDict in tableRes.Rows)
                {
                    var arr = new object?[tableRes.Columns.Count];
                    for (int i = 0; i < tableRes.Columns.Count; i++)
                    {
                        arr[i] = rowDict.TryGetValue(tableRes.Columns[i].ColumnName, out var v) ? v : null;
                    }
                    result.Rows.Add(arr);
                }

                result.AffectedRows = tableRes.Rows.Count;
                result.IsSuccess = true;
                return result;
            }

            // 2. SHOW DATABASES / SHOW COLLECTIONS
            if (trimmed.Equals("SHOW DATABASES", StringComparison.OrdinalIgnoreCase))
            {
                var dbs = await ListDatabasesAsync(ct);
                result.Columns.Add(new QueryResultColumn { Name = "Database", Type = "String", Ordinal = 0 });
                foreach (var d in dbs) result.Rows.Add([d]);
                result.AffectedRows = dbs.Count;
                result.IsSuccess = true;
                return result;
            }
            if (trimmed.Equals("SHOW COLLECTIONS", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("SHOW TABLES", StringComparison.OrdinalIgnoreCase))
            {
                var tables = await ListTablesAsync(null, ct);
                result.Columns.Add(new QueryResultColumn { Name = "Collection", Type = "String", Ordinal = 0 });
                foreach (var t in tables) result.Rows.Add([t.Name]);
                result.AffectedRows = tables.Count;
                result.IsSuccess = true;
                return result;
            }

            // 3. MQL find syntax: db.collection.find(...)
            var mqlFindMatch = Regex.Match(trimmed, @"^db\.(\w+)\.find\((.*?)\)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (mqlFindMatch.Success)
            {
                var collName = mqlFindMatch.Groups[1].Value;
                var filterJson = mqlFindMatch.Groups[2].Value.Trim();
                var tableRes = await SelectTopAsync(new SelectTopOptions { Table = collName, RawWhereClause = filterJson, Limit = 100 }, ct);

                result.Columns = tableRes.Columns.Select((c, i) => new QueryResultColumn
                {
                    Name = c.ColumnName,
                    Type = c.DataType,
                    Ordinal = i
                }).ToList();

                foreach (var rowDict in tableRes.Rows)
                {
                    var arr = new object?[tableRes.Columns.Count];
                    for (int i = 0; i < tableRes.Columns.Count; i++)
                    {
                        arr[i] = rowDict.TryGetValue(tableRes.Columns[i].ColumnName, out var v) ? v : null;
                    }
                    result.Rows.Add(arr);
                }

                result.AffectedRows = tableRes.Rows.Count;
                result.IsSuccess = true;
                return result;
            }

            // 4. Raw BSON Command: { ping: 1 } or { find: "users" }
            BsonDocument cmdDoc;
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                cmdDoc = BsonDocument.Parse(trimmed);
            }
            else
            {
                throw new ArgumentException("Unsupported MongoDB command format. Enter a JSON command e.g. { \"ping\": 1 } or db.collection.find() or SELECT * FROM collection");
            }

            var cmdResult = await _currentDatabase!.RunCommandAsync<BsonDocument>(cmdDoc, cancellationToken: ct);

            // Format response
            if (cmdResult.Contains("cursor") && cmdResult["cursor"].IsBsonDocument)
            {
                var cursorDoc = cmdResult["cursor"].AsBsonDocument;
                if (cursorDoc.Contains("firstBatch") && cursorDoc["firstBatch"].IsBsonArray)
                {
                    var batch = cursorDoc["firstBatch"].AsBsonArray;
                    var colSet = new List<string>();
                    foreach (var item in batch)
                    {
                        if (item.IsBsonDocument)
                        {
                            foreach (var elem in item.AsBsonDocument.Elements)
                            {
                                if (!colSet.Contains(elem.Name)) colSet.Add(elem.Name);
                            }
                        }
                    }

                    result.Columns = colSet.Select((c, i) => new QueryResultColumn { Name = c, Type = "BsonValue", Ordinal = i }).ToList();
                    foreach (var item in batch)
                    {
                        if (item.IsBsonDocument)
                        {
                            var row = new object?[colSet.Count];
                            var doc = item.AsBsonDocument;
                            for (int i = 0; i < colSet.Count; i++)
                            {
                                row[i] = doc.Contains(colSet[i]) ? FormatBsonValue(doc[colSet[i]]) : null;
                            }
                            result.Rows.Add(row);
                        }
                    }
                    result.AffectedRows = result.Rows.Count;
                    result.IsSuccess = true;
                    return result;
                }
            }

            // Single document command response
            result.Columns = cmdResult.Names.Select((name, i) => new QueryResultColumn { Name = name, Type = "BsonValue", Ordinal = i }).ToList();
            var singleRow = new object?[cmdResult.ElementCount];
            int elemIdx = 0;
            foreach (var elem in cmdResult.Elements)
            {
                singleRow[elemIdx++] = FormatBsonValue(elem.Value);
            }
            result.Rows.Add(singleRow);
            result.AffectedRows = 1;
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

    public async Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        var clean = sql.Trim();
        // In MongoDB, we can run explain command or return explanation metadata
        return await ExecuteQueryAsync(clean, ct);
    }

    public async IAsyncEnumerable<object?[]> StreamQueryAsync(string sql, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var res = await ExecuteQueryAsync(sql, ct);
        foreach (var row in res.Rows)
        {
            yield return row;
        }
    }

    public async Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "admin" : schema;
        var collection = _client!.GetDatabase(dbName).GetCollection<BsonDocument>(table);
        await collection.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
    }

    public async Task DropTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var dbName = string.IsNullOrWhiteSpace(schema) ? Config.DefaultDatabase ?? "admin" : schema;
        await _client!.GetDatabase(dbName).DropCollectionAsync(table, ct);
    }

    public Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult($"// MongoDB Collection\ndb.createCollection(\"{table}\");");
    }

    public ValueTask DisposeAsync()
    {
        _client = null;
        _currentDatabase = null;
        return ValueTask.CompletedTask;
    }

    private static object? FormatBsonValue(BsonValue val)
    {
        if (val == null || val.IsBsonNull) return null;
        if (val.IsObjectId) return val.AsObjectId.ToString();
        if (val.IsString) return val.AsString;
        if (val.IsInt32) return val.AsInt32;
        if (val.IsInt64) return val.AsInt64;
        if (val.IsDouble) return val.AsDouble;
        if (val.IsBoolean) return val.AsBoolean;
        if (val.IsValidDateTime) return val.ToUniversalTime();
        if (val.IsBsonDocument || val.IsBsonArray) return val.ToJson();
        return val.ToString();
    }

    private static FilterDefinition<BsonDocument> BuildIdFilter(object idVal)
    {
        var idStr = idVal.ToString() ?? "";
        if (ObjectId.TryParse(idStr, out var oid))
        {
            return Builders<BsonDocument>.Filter.Eq("_id", oid);
        }
        if (int.TryParse(idStr, out var idInt))
        {
            return Builders<BsonDocument>.Filter.Eq("_id", idInt);
        }
        if (long.TryParse(idStr, out var idLong))
        {
            return Builders<BsonDocument>.Filter.Eq("_id", idLong);
        }
        return Builders<BsonDocument>.Filter.Eq("_id", idStr);
    }

    private static BsonValue ConvertToBsonValue(object? val)
    {
        if (val == null || val is DBNull) return BsonNull.Value;
        if (val is string s)
        {
            if (s.StartsWith("{") && s.EndsWith("}"))
            {
                try { return BsonDocument.Parse(s); } catch { }
            }
            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                try { return BsonSerializer.Deserialize<BsonArray>(s); } catch { }
            }
            return new BsonString(s);
        }
        if (val is int i) return new BsonInt32(i);
        if (val is long l) return new BsonInt64(l);
        if (val is double d) return new BsonDouble(d);
        if (val is float f) return new BsonDouble(f);
        if (val is decimal dec) return new BsonDecimal128(dec);
        if (val is bool b) return new BsonBoolean(b);
        if (val is DateTime dt) return new BsonDateTime(dt);
        if (val is Guid g) return new BsonString(g.ToString());
        return BsonValue.Create(val);
    }
}
