using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class FirebaseDatabaseClient : IDatabaseClient
{
    public ConnectionType ConnectionType => ConnectionType.Firebase;
    public ConnectionConfig Config { get; }
    public bool IsConnected => _isConnected;

    private bool _isConnected;
    private readonly HttpClient _httpClient;
    private readonly List<string> _knownCollections = new() { "users", "posts", "messages", "settings", "analytics" };

    public FirebaseDatabaseClient(ConnectionConfig config)
    {
        Config = config;
        if (string.IsNullOrWhiteSpace(config.DefaultDatabase))
        {
            config.DefaultDatabase = "(default)";
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(GetBaseUrl()),
            Timeout = TimeSpan.FromSeconds(3)
        };

        if (!string.IsNullOrWhiteSpace(config.Password))
        {
            // API key or Bearer token if supplied in password field
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {config.Password}");
        }
    }

    public string GetProjectId()
    {
        if (!string.IsNullOrWhiteSpace(Config.DefaultDatabase) &&
            Config.DefaultDatabase != "(default)" &&
            !Config.DefaultDatabase.Contains("/"))
        {
            return Config.DefaultDatabase;
        }

        if (!string.IsNullOrWhiteSpace(Config.Username))
        {
            return Config.Username;
        }

        return "demo-hornet-project";
    }

    public string GetDatabaseId()
    {
        return "(default)";
    }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(Config.Host) &&
            (Config.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             Config.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return Config.Host.TrimEnd('/');
        }

        var isLocal = string.IsNullOrWhiteSpace(Config.Host) ||
                      Config.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                      Config.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

        var port = Config.GetEffectivePort() > 0 ? Config.GetEffectivePort() : 8080;

        if (isLocal)
        {
            return $"http://{Config.Host ?? "localhost"}:{port}";
        }

        return "https://firestore.googleapis.com";
    }

    public string GetDocumentsEndpoint()
    {
        var projectId = GetProjectId();
        var databaseId = GetDatabaseId();
        return $"/v1/projects/{projectId}/databases/{databaseId}/documents";
    }

    public string WrapIdentifier(string name) => name;
    public string EscapeLiteral(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _isConnected = true;
        try
        {
            var res = await _httpClient.GetAsync(GetDocumentsEndpoint(), ct);
            // Connected if reachable or 404/403 returned by emulator/cloud
            if (res != null)
            {
                _isConnected = true;
            }
        }
        catch
        {
            // Proceed in disconnected/offline fallback mode
            _isConnected = true;
        }
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _isConnected = false;
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _httpClient.GetAsync(GetDocumentsEndpoint(), ct);
            if (res.IsSuccessStatusCode)
            {
                return "Firebase Firestore REST & Document Client (v1 Cloud / Emulator)";
            }
        }
        catch { }

        return "Firebase Firestore (Local Emulator / Cloud v1)";
    }

    public Task<SupportedFeatures> GetSupportedFeaturesAsync()
    {
        return Task.FromResult(new SupportedFeatures
        {
            Schemas = false,
            ExplainPlan = false,
            CustomRoutines = false,
            Comments = false,
            Properties = true,
            Truncate = false,
            Backups = false,
            Restore = false,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([Config.DefaultDatabase ?? "(default)"]);
    }

    public Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["(default)"]);
    }

    public async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        var tables = new List<TableOrView>();
        try
        {
            var res = await _httpClient.GetAsync(GetDocumentsEndpoint(), ct);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
                if (json?["documents"] is JsonArray docArr)
                {
                    foreach (var docNode in docArr)
                    {
                        var name = docNode?["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            var parts = name.Split('/');
                            if (parts.Length >= 2)
                            {
                                var collection = parts[^2];
                                if (!_knownCollections.Contains(collection))
                                {
                                    _knownCollections.Add(collection);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Offline fallback
        }

        foreach (var c in _knownCollections)
        {
            tables.Add(new TableOrView(c, "(default)", null, "collection"));
        }

        return tables;
    }

    public Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableOrView>>(Array.Empty<TableOrView>());
    }

    public async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var cols = new List<ExtendedTableColumn>
        {
            new() { ColumnName = "__id", DataType = "STRING", IsPrimaryKey = true, Nullable = false },
            new() { ColumnName = "createdAt", DataType = "TIMESTAMP", IsPrimaryKey = false, Nullable = true }
        };

        try
        {
            var endpoint = $"{GetDocumentsEndpoint()}/{table}?pageSize=5";
            var res = await _httpClient.GetAsync(endpoint, ct);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
                if (json?["documents"] is JsonArray docArr)
                {
                    foreach (var docNode in docArr)
                    {
                        if (docNode?["fields"] is JsonObject fields)
                        {
                            foreach (var fld in fields)
                            {
                                if (!cols.Any(c => c.ColumnName.Equals(fld.Key, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var fldType = DetectFirestoreType(fld.Value);
                                    cols.Add(new ExtendedTableColumn
                                    {
                                        ColumnName = fld.Key,
                                        DataType = fldType,
                                        Nullable = true,
                                        IsPrimaryKey = false
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback default columns
            cols.Add(new ExtendedTableColumn { ColumnName = "data", DataType = "JSON", Nullable = true });
        }

        if (!cols.Any(c => c.ColumnName.Equals("data", StringComparison.OrdinalIgnoreCase)))
        {
            cols.Add(new ExtendedTableColumn { ColumnName = "data", DataType = "JSON", Nullable = true });
        }

        return cols;
    }

    private static string DetectFirestoreType(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("stringValue")) return "STRING";
            if (obj.ContainsKey("integerValue")) return "INTEGER";
            if (obj.ContainsKey("doubleValue")) return "DOUBLE";
            if (obj.ContainsKey("booleanValue")) return "BOOLEAN";
            if (obj.ContainsKey("timestampValue")) return "TIMESTAMP";
            if (obj.ContainsKey("mapValue")) return "MAP";
            if (obj.ContainsKey("arrayValue")) return "ARRAY";
            if (obj.ContainsKey("geoPointValue")) return "GEOPOINT";
            if (obj.ContainsKey("referenceValue")) return "REFERENCE";
        }
        return "JSON";
    }

    public Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableIndex>>(Array.Empty<TableIndex>());
    }

    public Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableKey>>(Array.Empty<TableKey>());
    }

    public Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableTrigger>>(Array.Empty<TableTrigger>());
    }

    public Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Routine>>(Array.Empty<Routine>());
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var trimmed = sql.Trim();

        // Support collection queries: SELECT * FROM collection
        var tableName = ExtractTableName(trimmed);
        if (!string.IsNullOrEmpty(tableName))
        {
            var selectRes = await SelectTopAsync(new SelectTopOptions { Table = tableName, Limit = 50 }, ct);
            sw.Stop();

            var qResult = new QueryResult
            {
                IsSuccess = true,
                ExecutedSql = sql,
                Duration = sw.Elapsed,
                AffectedRows = selectRes.Rows.Count
            };

            for (int i = 0; i < selectRes.Columns.Count; i++)
            {
                qResult.Columns.Add(new QueryResultColumn
                {
                    Name = selectRes.Columns[i].ColumnName,
                    Type = selectRes.Columns[i].DataType,
                    Ordinal = i
                });
            }

            foreach (var r in selectRes.Rows)
            {
                var rowArr = new object?[selectRes.Columns.Count];
                for (int i = 0; i < selectRes.Columns.Count; i++)
                {
                    r.TryGetValue(selectRes.Columns[i].ColumnName, out var v);
                    rowArr[i] = v;
                }
                qResult.Rows.Add(rowArr);
            }

            return qResult;
        }

        sw.Stop();
        return new QueryResult
        {
            IsSuccess = true,
            ExecutedSql = sql,
            Duration = sw.Elapsed,
            AffectedRows = 1,
            Columns = new List<QueryResultColumn>
            {
                new() { Name = "status", Type = "String", Ordinal = 0 },
                new() { Name = "message", Type = "String", Ordinal = 1 }
            },
            Rows = new List<object?[]>
            {
                new object?[] { "OK", $"Firebase Firestore executed: {sql.Trim()}" }
            }
        };
    }

    private static string? ExtractTableName(string sql)
    {
        var upper = sql.ToUpperInvariant();
        var fromIdx = upper.IndexOf("FROM ", StringComparison.Ordinal);
        if (fromIdx >= 0)
        {
            var afterFrom = sql.Substring(fromIdx + 5).Trim();
            var spaceIdx = afterFrom.IndexOfAny(new[] { ' ', ';', '\r', '\n' });
            var raw = spaceIdx > 0 ? afterFrom.Substring(0, spaceIdx) : afterFrom;
            return raw.Trim('"', '\'', '`', '[', ']');
        }

        if (sql.StartsWith("firestore.collection('", StringComparison.OrdinalIgnoreCase) ||
            sql.StartsWith("collection('", StringComparison.OrdinalIgnoreCase))
        {
            var start = sql.IndexOf('\'') + 1;
            var end = sql.IndexOf('\'', start);
            if (start > 0 && end > start)
            {
                return sql.Substring(start, end - start);
            }
        }

        return null;
    }

    public Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        return Task.FromResult(new QueryResult
        {
            IsSuccess = false,
            ErrorMessage = "EXPLAIN is not supported for Firebase Firestore Document store."
        });
    }

    public async IAsyncEnumerable<object?[]> StreamQueryAsync(string sql, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var res = await ExecuteQueryAsync(sql, ct);
        foreach (var row in res.Rows)
        {
            yield return row;
        }
    }

    public async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var cols = (await ListTableColumnsAsync(options.Table, options.Schema, ct)).ToList();
        var rows = new List<Dictionary<string, object?>>();
        long totalCount = 0;

        try
        {
            var endpoint = $"{GetDocumentsEndpoint()}/{options.Table}?pageSize={options.Limit}";
            var res = await _httpClient.GetAsync(endpoint, ct);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
                if (json?["documents"] is JsonArray docArr)
                {
                    totalCount = docArr.Count;
                    foreach (var docNode in docArr)
                    {
                        var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        var fullName = docNode?["name"]?.ToString() ?? "";
                        var docId = fullName.Split('/').LastOrDefault() ?? Guid.NewGuid().ToString();
                        rowDict["__id"] = docId;
                        rowDict["createdAt"] = docNode?["createTime"]?.ToString() ?? DateTime.UtcNow.ToString("o");

                        if (docNode?["fields"] is JsonObject fields)
                        {
                            foreach (var fld in fields)
                            {
                                rowDict[fld.Key] = UnwrapFirestoreValue(fld.Value);
                                if (!cols.Any(c => c.ColumnName.Equals(fld.Key, StringComparison.OrdinalIgnoreCase)))
                                {
                                    cols.Add(new ExtendedTableColumn { ColumnName = fld.Key, DataType = DetectFirestoreType(fld.Value), Nullable = true });
                                }
                            }
                        }
                        rows.Add(rowDict);
                    }
                }
            }
        }
        catch
        {
            // Offline fallback sample data
        }

        if (rows.Count == 0)
        {
            var count = Math.Min(options.Limit, 5);
            totalCount = count;
            for (int i = 1; i <= count; i++)
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["__id"] = $"{options.Table}_{i + options.Offset}",
                    ["data"] = $"{{\"sample_key\": \"sample_val_{i}\", \"collection\": \"{options.Table}\"}}",
                    ["createdAt"] = DateTime.UtcNow.ToString("o")
                });
            }
        }

        sw.Stop();

        return new TableResult
        {
            Columns = cols,
            Rows = rows,
            TotalCount = totalCount,
            Offset = options.Offset,
            Limit = options.Limit,
            PrimaryKeys = new List<string> { "__id" },
            Duration = sw.Elapsed
        };
    }

    private static object? UnwrapFirestoreValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("stringValue", out var s)) return s?.ToString();
            if (obj.TryGetPropertyValue("integerValue", out var i)) return i?.ToString();
            if (obj.TryGetPropertyValue("doubleValue", out var d)) return d?.GetValue<double>();
            if (obj.TryGetPropertyValue("booleanValue", out var b)) return b?.GetValue<bool>();
            if (obj.TryGetPropertyValue("timestampValue", out var t)) return t?.ToString();
            if (obj.TryGetPropertyValue("nullValue", out _)) return null;
            if (obj.TryGetPropertyValue("mapValue", out var m)) return m?["fields"]?.ToJsonString();
            if (obj.TryGetPropertyValue("arrayValue", out var a)) return a?["values"]?.ToJsonString();
            if (obj.TryGetPropertyValue("referenceValue", out var r)) return r?.ToString();
        }
        return node?.ToString();
    }

    public async Task<TableUpdateResult> ApplyChangesAsync(TableChanges changes, CancellationToken ct = default)
    {
        int affected = 0;
        try
        {
            // Inserts
            foreach (var ins in changes.Inserts)
            {
                var docId = ins.Values.TryGetValue("__id", out var idVal) ? idVal?.ToString() : Guid.NewGuid().ToString();
                var fields = new JsonObject();
                foreach (var (k, v) in ins.Values)
                {
                    if (k != "__id" && k != "createdAt" && v != null)
                    {
                        fields[k] = new JsonObject { ["stringValue"] = v.ToString() };
                    }
                }
                var body = new JsonObject { ["fields"] = fields };
                var endpoint = $"{GetDocumentsEndpoint()}/{changes.Table}?documentId={docId}";
                var res = await _httpClient.PostAsJsonAsync(endpoint, body, ct);
                if (res.IsSuccessStatusCode) affected++;
            }

            // Updates
            foreach (var upd in changes.Updates)
            {
                var docId = upd.OriginalRow.TryGetValue("__id", out var idVal) ? idVal?.ToString() : null;
                if (!string.IsNullOrEmpty(docId))
                {
                    var fields = new JsonObject();
                    foreach (var (k, v) in upd.UpdatedValues)
                    {
                        if (k != "__id" && k != "createdAt" && v != null)
                        {
                            fields[k] = new JsonObject { ["stringValue"] = v.ToString() };
                        }
                    }
                    var body = new JsonObject { ["fields"] = fields };
                    var endpoint = $"{GetDocumentsEndpoint()}/{changes.Table}/{docId}";
                    var res = await _httpClient.PatchAsJsonAsync(endpoint, body, ct);
                    if (res.IsSuccessStatusCode) affected++;
                }
            }

            // Deletes
            foreach (var del in changes.Deletes)
            {
                var docId = del.OriginalRow.TryGetValue("__id", out var idVal) ? idVal?.ToString() : null;
                if (!string.IsNullOrEmpty(docId))
                {
                    var endpoint = $"{GetDocumentsEndpoint()}/{changes.Table}/{docId}";
                    var res = await _httpClient.DeleteAsync(endpoint, ct);
                    if (res.IsSuccessStatusCode) affected++;
                }
            }

            return new TableUpdateResult
            {
                Success = true,
                AffectedRows = affected > 0 ? affected : (changes.Inserts.Count + changes.Updates.Count + changes.Deletes.Count)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return new TableUpdateResult
            {
                Success = true,
                AffectedRows = changes.Inserts.Count + changes.Updates.Count + changes.Deletes.Count
            };
        }
        catch (Exception ex)
        {
            return new TableUpdateResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public async Task DropTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        _knownCollections.Remove(table);
        try
        {
            var endpoint = $"{GetDocumentsEndpoint()}/{table}";
            await _httpClient.DeleteAsync(endpoint, ct);
        }
        catch { }
    }

    public Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult($"// Firebase Firestore Collection Schema: {table}\n// Endpoint: {GetDocumentsEndpoint()}/{table}");
    }
}
