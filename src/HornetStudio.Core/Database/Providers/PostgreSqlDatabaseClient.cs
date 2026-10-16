using System.Data;
using Npgsql;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class PostgreSqlDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.PostgreSql;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public PostgreSqlDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Config.Host,
            Port = Config.GetEffectivePort(),
            Database = Config.DefaultDatabase ?? "postgres",
            Username = Config.Username ?? "postgres",
            Password = Config.Password ?? "",
            Timeout = 15,
            CommandTimeout = 120
        };

        if (Config.Ssl.Enabled)
        {
            builder.SslMode = Config.Ssl.RejectUnauthorized ? SslMode.VerifyFull : SslMode.Require;
        }

        var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT version()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "PostgreSQL";
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
            CtidSupported = true
        });
    }

    public override async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var list = new List<string>();
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname";
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
        cmd.CommandText = "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ('pg_catalog', 'information_schema') ORDER BY schema_name";
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
        var targetSchema = schema ?? "public";
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'BASE TABLE' ORDER BY table_name";
        var p = cmd.CreateParameter();
        p.ParameterName = "@schema";
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), targetSchema, Config.DefaultDatabase, "table"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "public";
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'VIEW' ORDER BY table_name";
        var p = cmd.CreateParameter();
        p.ParameterName = "@schema";
        p.Value = targetSchema;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), targetSchema, Config.DefaultDatabase, "view"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "public";
        var list = new List<ExtendedTableColumn>();

        var pks = await GetPrimaryKeyColumnNames(table, targetSchema, ct);

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, is_nullable, column_default, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colName = reader["column_name"].ToString() ?? "";
            var isPk = pks.Contains(colName);
            list.Add(new ExtendedTableColumn
            {
                ColumnName = colName,
                DataType = reader["data_type"]?.ToString() ?? "text",
                Nullable = reader["is_nullable"]?.ToString() == "YES",
                DefaultValue = reader["column_default"] is DBNull ? null : reader["column_default"]?.ToString(),
                IsPrimaryKey = isPk,
                MaxLength = reader["character_maximum_length"] is int m ? m : null
            });
        }

        return list;
    }

    private async Task<HashSet<string>> GetPrimaryKeyColumnNames(string table, string schema, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
              AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
              AND tc.table_schema = @schema
              AND tc.table_name = @table";

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
        var targetSchema = schema ?? "public";
        var list = new List<TableIndex>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = @schema AND tablename = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader["indexname"].ToString() ?? "";
            var def = reader["indexdef"].ToString() ?? "";
            list.Add(new TableIndex
            {
                Name = name,
                IsUnique = def.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase),
                IndexType = "BTREE"
            });
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "public";
        var list = new List<TableKey>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.constraint_name, kcu.column_name,
                ccu.table_name AS foreign_table_name,
                ccu.column_name AS foreign_column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON tc.constraint_name = kcu.constraint_name
              AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
              AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = @schema
              AND tc.table_name = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableKey
            {
                ConstraintName = reader["constraint_name"].ToString() ?? "",
                ColumnName = reader["column_name"].ToString() ?? "",
                ReferencedTable = reader["foreign_table_name"].ToString() ?? "",
                ReferencedColumn = reader["foreign_column_name"].ToString() ?? ""
            });
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var targetSchema = schema ?? "public";
        var list = new List<TableTrigger>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT trigger_name, event_manipulation, action_timing, action_statement
            FROM information_schema.triggers
            WHERE event_object_schema = @schema AND event_object_table = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = targetSchema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableTrigger
            {
                Name = reader["trigger_name"].ToString() ?? "",
                Event = reader["event_manipulation"].ToString() ?? "",
                Timing = reader["action_timing"].ToString() ?? "",
                Definition = reader["action_statement"].ToString()
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
            SELECT routine_name, routine_type, data_type, routine_schema
            FROM information_schema.routines
            WHERE routine_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY routine_name";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Routine
            {
                Name = reader["routine_name"].ToString() ?? "",
                RoutineType = reader["routine_type"].ToString() ?? "FUNCTION",
                ReturnType = reader["data_type"]?.ToString(),
                Schema = reader["routine_schema"]?.ToString()
            });
        }
        return list;
    }

    public override Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        var targetSchema = schema ?? "public";
        return Task.FromResult($"-- DDL dla PostgreSQL {targetSchema}.{table}\n-- Użyj narzędzia eksportu HornetStudio do pełnego skryptu DDL.");
    }
}
