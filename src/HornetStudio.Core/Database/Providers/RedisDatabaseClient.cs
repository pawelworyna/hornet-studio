using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using StackExchange.Redis;

namespace HornetStudio.Core.Database.Providers;

public class RedisDatabaseClient : IDatabaseClient
{
    public HornetStudio.Core.Domain.Enums.ConnectionType ConnectionType => HornetStudio.Core.Domain.Enums.ConnectionType.Redis;
    public ConnectionConfig Config { get; }
    public bool IsConnected => _mux != null && _mux.IsConnected;

    private IConnectionMultiplexer? _mux;
    private int _defaultDatabaseIndex;

    public RedisDatabaseClient(ConnectionConfig config)
    {
        Config = config;
        if (int.TryParse(config.DefaultDatabase, out var dbIdx) && dbIdx >= 0)
        {
            _defaultDatabaseIndex = dbIdx;
        }
    }

    public string WrapIdentifier(string name) => name;
    public string EscapeLiteral(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_mux != null && _mux.IsConnected) return;

        ConfigurationOptions options;
        if (Config.UseConnectionString && !string.IsNullOrWhiteSpace(Config.ConnectionString))
        {
            options = ConfigurationOptions.Parse(Config.ConnectionString);
        }
        else
        {
            var host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host;
            var port = Config.GetEffectivePort();
            if (port <= 0) port = 6379;

            options = new ConfigurationOptions
            {
                EndPoints = { { host, port } },
                Password = string.IsNullOrEmpty(Config.Password) ? null : Config.Password,
                User = string.IsNullOrEmpty(Config.Username) ? null : Config.Username,
                Ssl = Config.Ssl.Enabled,
                ConnectTimeout = 6000,
                SyncTimeout = 6000,
                AbortOnConnectFail = false,
                DefaultDatabase = _defaultDatabaseIndex
            };
        }

        _mux = await ConnectionMultiplexer.ConnectAsync(options);
    }

    public async Task DisconnectAsync()
    {
        if (_mux != null)
        {
            await _mux.CloseAsync();
            _mux.Dispose();
            _mux = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    private IDatabase GetDb(string? schema = null)
    {
        if (_mux == null) throw new InvalidOperationException("Not connected to Redis server.");

        if (!string.IsNullOrWhiteSpace(schema))
        {
            var match = Regex.Match(schema, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var idx))
            {
                return _mux.GetDatabase(idx);
            }
        }

        return _mux.GetDatabase(_defaultDatabaseIndex);
    }

    private IServer GetServer()
    {
        if (_mux == null) throw new InvalidOperationException("Not connected to Redis server.");
        var endpoints = _mux.GetEndPoints();
        if (endpoints.Length == 0) throw new InvalidOperationException("No Redis endpoints available.");
        return _mux.GetServer(endpoints[0]);
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var server = GetServer();
            return $"Redis {server.Version} ({server.ServerType})";
        }
        catch
        {
            return "Redis Server (Connected)";
        }
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
            ExplainPlan = false,
            RowIdSupported = false,
            CtidSupported = false
        });
    }

    public Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        // Redis standard logical databases db0 through db15
        var list = new List<string>();
        for (int i = 0; i < 16; i++)
        {
            list.Add($"db{i}");
        }
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        var targetDb = schema ?? $"db{_defaultDatabaseIndex}";
        var tables = new List<TableOrView>
        {
            new("keys", targetDb, targetDb, "table")
        };
        return Task.FromResult<IReadOnlyList<TableOrView>>(tables);
    }

    public Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TableOrView>>([]);
    }

    public Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var cols = new List<ExtendedTableColumn>
        {
            new() { ColumnName = "key", DataType = "string", IsPrimaryKey = true, Nullable = false },
            new() { ColumnName = "type", DataType = "string", IsPrimaryKey = false, Nullable = false },
            new() { ColumnName = "ttl", DataType = "int64", IsPrimaryKey = false, Nullable = false },
            new() { ColumnName = "length", DataType = "int64", IsPrimaryKey = false, Nullable = false },
            new() { ColumnName = "value", DataType = "string", IsPrimaryKey = false, Nullable = true }
        };
        return Task.FromResult<IReadOnlyList<ExtendedTableColumn>>(cols);
    }

    public Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TableIndex>>([]);

    public Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TableKey>>([]);

    public Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TableTrigger>>([]);

    public Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Routine>>([]);

    public async Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var db = GetDb(options.Schema);
        var server = GetServer();

        var dbIndex = _defaultDatabaseIndex;
        if (!string.IsNullOrWhiteSpace(options.Schema))
        {
            var match = Regex.Match(options.Schema, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var idx))
            {
                dbIndex = idx;
            }
        }

        string pattern = "*";
        if (options.Filters.Count > 0)
        {
            var keyFilter = options.Filters.FirstOrDefault(f => f.Field.Equals("key", StringComparison.OrdinalIgnoreCase));
            if (keyFilter != null && keyFilter.Value != null)
            {
                var val = keyFilter.Value.ToString() ?? "*";
                pattern = val.Contains('*') ? val : $"*{val}*";
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.RawWhereClause))
        {
            pattern = options.RawWhereClause.Trim();
            if (!pattern.Contains('*')) pattern = $"*{pattern}*";
        }

        var offset = Math.Max(0, options.Offset);
        var limit = options.Limit <= 0 ? 100 : Math.Min(options.Limit, 1000);

        var keys = new List<RedisKey>();
        try
        {
            foreach (var k in server.Keys(database: dbIndex, pattern: pattern, pageSize: 250))
            {
                keys.Add(k);
                if (keys.Count >= offset + limit) break;
            }
        }
        catch
        {
            // Fallback if KEYS/SCAN is restricted
        }

        var pageKeys = keys.Skip(offset).Take(limit).ToList();
        var rows = new List<Dictionary<string, object?>>();

        foreach (var key in pageKeys)
        {
            var keyStr = (string)key!;
            RedisType redisType;
            try
            {
                redisType = await db.KeyTypeAsync(key);
            }
            catch
            {
                redisType = RedisType.None;
            }

            TimeSpan? ttlSpan = null;
            try
            {
                ttlSpan = await db.KeyTimeToLiveAsync(key);
            }
            catch { }

            long ttlSeconds = ttlSpan.HasValue ? (long)ttlSpan.Value.TotalSeconds : -1;
            long length = 0;
            string? valueDisplay = string.Empty;

            try
            {
                switch (redisType)
                {
                    case RedisType.String:
                        var strVal = await db.StringGetAsync(key);
                        valueDisplay = strVal.HasValue ? (string?)strVal : string.Empty;
                        length = valueDisplay?.Length ?? 0;
                        break;

                    case RedisType.Hash:
                        var hashEntries = await db.HashGetAllAsync(key);
                        length = hashEntries.Length;
                        valueDisplay = JsonSerializer.Serialize(hashEntries.ToDictionary(h => (string)h.Name!, h => (string)h.Value!));
                        break;

                    case RedisType.List:
                        length = await db.ListLengthAsync(key);
                        var listItems = await db.ListRangeAsync(key, 0, 50);
                        valueDisplay = JsonSerializer.Serialize(listItems.Select(i => (string?)i));
                        break;

                    case RedisType.Set:
                        length = await db.SetLengthAsync(key);
                        var setMembers = await db.SetMembersAsync(key);
                        valueDisplay = JsonSerializer.Serialize(setMembers.Take(50).Select(m => (string?)m));
                        break;

                    case RedisType.SortedSet:
                        length = await db.SortedSetLengthAsync(key);
                        var zsetItems = await db.SortedSetRangeByRankWithScoresAsync(key, 0, 50);
                        valueDisplay = JsonSerializer.Serialize(zsetItems.Select(z => new { element = (string)z.Element!, score = z.Score }));
                        break;

                    default:
                        valueDisplay = $"<{redisType}>";
                        break;
                }
            }
            catch (Exception ex)
            {
                valueDisplay = $"<Error reading value: {ex.Message}>";
            }

            rows.Add(new Dictionary<string, object?>
            {
                ["key"] = keyStr,
                ["type"] = redisType.ToString().ToLowerInvariant(),
                ["ttl"] = ttlSeconds,
                ["length"] = length,
                ["value"] = valueDisplay
            });
        }

        sw.Stop();

        return new TableResult
        {
            Columns = (await ListTableColumnsAsync(options.Table, options.Schema, ct)).ToList(),
            Rows = rows,
            TotalCount = keys.Count,
            Offset = offset,
            Limit = limit,
            PrimaryKeys = ["key"],
            IdentifierMode = RowIdentifierMode.PrimaryKey,
            Duration = sw.Elapsed
        };
    }

    public async Task<TableUpdateResult> ApplyChangesAsync(TableChanges changes, CancellationToken ct = default)
    {
        var db = GetDb(changes.Schema);
        int affected = 0;

        try
        {
            // 1. Deletions
            foreach (var del in changes.Deletes)
            {
                if (del.OriginalRow.TryGetValue("key", out var keyObj) && keyObj != null)
                {
                    var keyStr = keyObj.ToString();
                    if (!string.IsNullOrEmpty(keyStr))
                    {
                        var deleted = await db.KeyDeleteAsync(keyStr);
                        if (deleted) affected++;
                    }
                }
            }

            // 2. Inserts
            foreach (var ins in changes.Inserts)
            {
                if (ins.Values.TryGetValue("key", out var keyObj) && keyObj != null)
                {
                    var keyStr = keyObj.ToString();
                    if (!string.IsNullOrEmpty(keyStr))
                    {
                        var valStr = ins.Values.TryGetValue("value", out var v) ? v?.ToString() ?? "" : "";
                        Expiration expiry = default;
                        if (ins.Values.TryGetValue("ttl", out var ttlObj) &&
                            long.TryParse(ttlObj?.ToString(), out var ttlSec) && ttlSec > 0)
                        {
                            expiry = TimeSpan.FromSeconds(ttlSec);
                        }

                        await db.StringSetAsync(keyStr, valStr, expiry);
                        affected++;
                    }
                }
            }

            // 3. Updates
            foreach (var upd in changes.Updates)
            {
                var origKey = upd.OriginalRow.TryGetValue("key", out var ok) ? ok?.ToString() : null;
                var newKey = upd.UpdatedValues.TryGetValue("key", out var nk) ? nk?.ToString() : origKey;

                if (string.IsNullOrEmpty(origKey)) continue;

                if (!string.IsNullOrEmpty(newKey) && newKey != origKey)
                {
                    await db.KeyRenameAsync(origKey, newKey);
                }

                var keyToUpdate = newKey ?? origKey;

                if (upd.UpdatedValues.TryGetValue("value", out var valObj))
                {
                    await db.StringSetAsync(keyToUpdate, valObj?.ToString() ?? "");
                    affected++;
                }

                if (upd.UpdatedValues.TryGetValue("ttl", out var ttlObj) &&
                    long.TryParse(ttlObj?.ToString(), out var ttlSec))
                {
                    await db.KeyExpireAsync(keyToUpdate, ttlSec > 0 ? TimeSpan.FromSeconds(ttlSec) : null);
                    affected++;
                }
            }

            return new TableUpdateResult { Success = true, AffectedRows = affected };
        }
        catch (Exception ex)
        {
            return new TableUpdateResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var trimmed = sql.Trim();
        var db = GetDb();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new QueryResult { IsSuccess = true, Duration = TimeSpan.Zero };
        }

        var tokens = ParseTokens(trimmed);
        if (tokens.Count == 0)
        {
            return new QueryResult { IsSuccess = true, Duration = TimeSpan.Zero };
        }

        var command = tokens[0].ToUpperInvariant();
        var args = tokens.Skip(1).Select(t => (object)t).ToArray();

        try
        {
            if (command == "INFO")
            {
                var server = GetServer();
                string section = args.Length > 0 ? args[0].ToString() ?? "" : "";
                var info = await server.InfoAsync(section);
                sw.Stop();

                var rows = new List<object?[]>();
                foreach (var group in info)
                {
                    foreach (var pair in group)
                    {
                        rows.Add([group.Key, pair.Key, pair.Value]);
                    }
                }

                return new QueryResult
                {
                    IsSuccess = true,
                    Columns =
                    [
                        new QueryResultColumn { Name = "Section", Type = "String", Ordinal = 0 },
                        new QueryResultColumn { Name = "Key", Type = "String", Ordinal = 1 },
                        new QueryResultColumn { Name = "Value", Type = "String", Ordinal = 2 }
                    ],
                    Rows = rows,
                    Duration = sw.Elapsed,
                    ExecutedSql = trimmed
                };
            }

            var redisResult = await db.ExecuteAsync(command, args);
            sw.Stop();

            if (redisResult.IsNull)
            {
                return new QueryResult
                {
                    IsSuccess = true,
                    Columns = [new QueryResultColumn { Name = "Result", Type = "Null", Ordinal = 0 }],
                    Rows = [[null]],
                    Duration = sw.Elapsed,
                    ExecutedSql = trimmed
                };
            }

            if (redisResult.Resp2Type == ResultType.Array)
            {
                var bulkArray = (RedisResult[])redisResult!;
                var rows = bulkArray.Select((item, idx) => (object?[])[idx + 1, item.ToString()]).ToList();

                return new QueryResult
                {
                    IsSuccess = true,
                    Columns =
                    [
                        new QueryResultColumn { Name = "#", Type = "Int32", Ordinal = 0 },
                        new QueryResultColumn { Name = "Value", Type = "String", Ordinal = 1 }
                    ],
                    Rows = rows,
                    Duration = sw.Elapsed,
                    ExecutedSql = trimmed
                };
            }

            return new QueryResult
            {
                IsSuccess = true,
                Columns = [new QueryResultColumn { Name = "Result", Type = redisResult.Resp2Type.ToString(), Ordinal = 0 }],
                Rows = [[redisResult.ToString()]],
                Duration = sw.Elapsed,
                ExecutedSql = trimmed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new QueryResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed,
                ExecutedSql = trimmed
            };
        }
    }

    public Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default)
    {
        return Task.FromResult(new QueryResult
        {
            IsSuccess = false,
            ErrorMessage = "EXPLAIN execution plan is not supported for Redis Key-Value store."
        });
    }

    public async IAsyncEnumerable<object?[]> StreamQueryAsync(string sql, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await ExecuteQueryAsync(sql, ct);
        foreach (var row in result.Rows)
        {
            yield return row;
        }
    }

    public Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        return Task.FromResult($"# Redis Key-Value Store\n# Database: {schema ?? "db0"}\n# Use SET, HSET, LPUSH, SADD to create keys.");
    }

    public async Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var db = GetDb(schema);
        await db.ExecuteAsync("FLUSHDB");
    }

    public async Task DropTableAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var db = GetDb(schema);
        await db.ExecuteAsync("FLUSHDB");
    }

    private static List<string> ParseTokens(string command)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if ((c == '"' || c == '\'') && !inQuotes)
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                quoteChar = '\0';
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }
}
