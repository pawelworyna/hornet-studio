using System.Data;
using MySqlConnector;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Providers;

public class MySqlDatabaseClient : DatabaseClientBase
{
    public override ConnectionType ConnectionType => ConnectionType.MySql;
    public override bool IsConnected => CurrentConnection?.State == ConnectionState.Open;

    public MySqlDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override string WrapIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Config.Host,
            Port = (uint)Config.GetEffectivePort(),
            Database = Config.DefaultDatabase ?? "",
            UserID = Config.Username ?? "root",
            Password = Config.Password ?? "",
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 120,
            AllowUserVariables = true,
            AllowPublicKeyRetrieval = true
        };

        if (Config.Ssl.Enabled)
        {
            builder.SslMode = Config.Ssl.RejectUnauthorized ? MySqlSslMode.Required : MySqlSslMode.Preferred;
        }

        var conn = new MySqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = "SELECT version()";
        var res = await cmd.ExecuteScalarAsync(ct);
        return res?.ToString() ?? "MySQL";
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
            Schemas = false, // W MySQL baza = schemat
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
        cmd.CommandText = "SHOW DATABASES";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var db = reader.GetString(0);
            if (!db.Equals("information_schema", StringComparison.OrdinalIgnoreCase) &&
                !db.Equals("performance_schema", StringComparison.OrdinalIgnoreCase) &&
                !db.Equals("mysql", StringComparison.OrdinalIgnoreCase) &&
                !db.Equals("sys", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(db);
            }
        }
        return list;
    }

    public override Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public override async Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = @db AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        var p = cmd.CreateParameter();
        p.ParameterName = "@db";
        p.Value = db;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), null, db, "table"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<TableOrView>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = @db AND table_type = 'VIEW'
            ORDER BY table_name";

        var p = cmd.CreateParameter();
        p.ParameterName = "@db";
        p.Value = db;
        cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableOrView(reader.GetString(0), null, db, "view"));
        }
        return list;
    }

    public override async Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<ExtendedTableColumn>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, is_nullable, column_default, column_key, extra, character_maximum_length, column_comment
            FROM information_schema.columns 
            WHERE table_schema = @db AND table_name = @tbl
            ORDER BY ordinal_position";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = db; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tbl"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader["column_key"]?.ToString();
            var extra = reader["extra"]?.ToString();
            list.Add(new ExtendedTableColumn
            {
                ColumnName = reader["column_name"].ToString() ?? "",
                DataType = reader["data_type"]?.ToString() ?? "varchar",
                Nullable = reader["is_nullable"]?.ToString() == "YES",
                DefaultValue = reader["column_default"] is DBNull ? null : reader["column_default"]?.ToString(),
                IsPrimaryKey = key == "PRI",
                IsAutoIncrement = extra?.Contains("auto_increment", StringComparison.OrdinalIgnoreCase) ?? false,
                MaxLength = reader["character_maximum_length"] is int m ? m : null,
                Comment = reader["column_comment"]?.ToString(),
                Extra = extra
            });
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var dict = new Dictionary<string, TableIndex>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT index_name, non_unique, column_name, index_type
            FROM information_schema.statistics 
            WHERE table_schema = @db AND table_name = @tbl
            ORDER BY seq_in_index";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = db; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tbl"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var idxName = reader["index_name"].ToString() ?? "";
            var nonUnique = Convert.ToInt32(reader["non_unique"]) == 1;
            var col = reader["column_name"].ToString() ?? "";
            var type = reader["index_type"]?.ToString();

            if (!dict.TryGetValue(idxName, out var idx))
            {
                idx = new TableIndex
                {
                    Name = idxName,
                    IsUnique = !nonUnique,
                    IsPrimaryKey = idxName == "PRIMARY",
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
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<TableKey>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT constraint_name, column_name, referenced_table_name, referenced_column_name
            FROM information_schema.key_column_usage
            WHERE table_schema = @db AND table_name = @tbl AND referenced_table_name IS NOT NULL";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = db; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tbl"; p2.Value = table; cmd.Parameters.Add(p2);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TableKey
            {
                ConstraintName = reader["constraint_name"].ToString() ?? "",
                ColumnName = reader["column_name"].ToString() ?? "",
                ReferencedTable = reader["referenced_table_name"].ToString() ?? "",
                ReferencedColumn = reader["referenced_column_name"].ToString() ?? ""
            });
        }
        return list;
    }

    public override async Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        var db = schema ?? Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<TableTrigger>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT trigger_name, event_manipulation, action_timing, action_statement
            FROM information_schema.triggers
            WHERE trigger_schema = @db AND event_object_table = @tbl";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = db; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tbl"; p2.Value = table; cmd.Parameters.Add(p2);

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
        var db = Config.DefaultDatabase ?? CurrentConnection!.Database;
        var list = new List<Routine>();

        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = @"
            SELECT routine_name, routine_type, dtd_identifier
            FROM information_schema.routines
            WHERE routine_schema = @db";

        var p = cmd.CreateParameter(); p.ParameterName = "@db"; p.Value = db; cmd.Parameters.Add(p);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Routine
            {
                Name = reader["routine_name"].ToString() ?? "",
                RoutineType = reader["routine_type"].ToString() ?? "PROCEDURE",
                ReturnType = reader["dtd_identifier"]?.ToString()
            });
        }
        return list;
    }

    public override async Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        using var cmd = CurrentConnection!.CreateCommand();
        cmd.CommandText = $"SHOW CREATE TABLE {WrapIdentifier(table)}";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return reader.GetString(1);
        }
        return $"CREATE TABLE {WrapIdentifier(table)} ();";
    }
}
