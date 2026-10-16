using System.Data;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using Npgsql;

namespace HornetStudio.Core.Database.Providers;

public class SupabaseDatabaseClient : PostgreSqlDatabaseClient
{
    public override ConnectionType ConnectionType => ConnectionType.Supabase;

    public SupabaseDatabaseClient(ConnectionConfig config) : base(config)
    {
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (CurrentConnection != null && CurrentConnection.State == ConnectionState.Open)
            return;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host,
            Port = Config.GetEffectivePort(),
            Database = Config.DefaultDatabase ?? "postgres",
            Username = Config.Username ?? "postgres",
            Password = Config.Password ?? "",
            Timeout = 15,
            CommandTimeout = 120,
            // Supabase cloud requires SSL
            SslMode = Config.Ssl.RejectUnauthorized ? SslMode.VerifyFull : SslMode.Require
        };

        var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        CurrentConnection = conn;
    }

    public override async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var raw = await base.GetVersionAsync(ct);
        return $"Supabase ({raw})";
    }

    public override async Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default)
    {
        var schemas = await base.ListSchemasAsync(ct);
        var result = new List<string>(schemas);
        // Prioritize common Supabase schemas
        var supabaseSchemas = new[] { "public", "auth", "storage", "realtime", "vault" };
        foreach (var s in supabaseSchemas)
        {
            if (!result.Contains(s, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(s);
            }
        }
        return result;
    }
}
