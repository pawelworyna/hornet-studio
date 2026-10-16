using Microsoft.Data.Sqlite;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Core.Storage.Security;
using Microsoft.EntityFrameworkCore;

namespace HornetStudio.Core.Storage.Migration;

public class ExternalImportResult
{
    public int ImportedConnections { get; set; }
    public int ImportedFolders { get; set; }
    public int ImportedQueries { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class ExternalConnectionImporter
{
    public static string GetDefaultExternalDbPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "external-studio",
            "connections.db");
    }

    public static bool ExternalDataExists()
    {
        return File.Exists(GetDefaultExternalDbPath());
    }

    public async Task<ExternalImportResult> ImportAsync(
        HornetDbContext targetDb,
        ISecretVault vault,
        string? customSourcePath = null,
        CancellationToken ct = default)
    {
        var result = new ExternalImportResult();
        var sourcePath = customSourcePath ?? GetDefaultExternalDbPath();

        if (!File.Exists(sourcePath))
        {
            result.Warnings.Add($"External connections database file does not exist: {sourcePath}");
            return result;
        }

        using var srcConn = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        await srcConn.OpenAsync(ct);

        // 1. Import connection folders
        var folderMap = new Dictionary<string, Guid>();
        try
        {
            using var cmd = srcConn.CreateCommand();
            cmd.CommandText = "SELECT id, name, color, position FROM connection_folder";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var oldId = reader["id"].ToString() ?? "";
                var name = reader["name"]?.ToString() ?? "Folder";
                var color = reader["color"]?.ToString();
                var pos = reader["position"] is long p ? (int)p : 0;

                var newFolder = new ConnectionFolderEntity
                {
                    Name = name,
                    Color = color,
                    Position = pos
                };
                targetDb.ConnectionFolders.Add(newFolder);
                folderMap[oldId] = newFolder.Id;
                result.ImportedFolders++;
            }
            await targetDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Warning during folder import: {ex.Message}");
        }

        // 2. Import saved connections
        try
        {
            using var cmd = srcConn.CreateCommand();
            cmd.CommandText = "SELECT name, connectionType, host, port, username, password, defaultDatabase, labelColor, connectionFolderId FROM saved_connection";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader["name"]?.ToString() ?? "Imported Connection";
                var rawType = reader["connectionType"]?.ToString()?.ToLowerInvariant();
                var host = reader["host"]?.ToString() ?? "localhost";
                var port = reader["port"] is long p ? (int?)p : null;
                var user = reader["username"]?.ToString();
                var pass = reader["password"]?.ToString();
                var dbName = reader["defaultDatabase"]?.ToString();
                var color = reader["labelColor"]?.ToString();
                var oldFolderId = reader["connectionFolderId"]?.ToString();

                var connType = rawType switch
                {
                    "postgres" or "postgresql" or "cockroachdb" or "redshift" => ConnectionType.PostgreSql,
                    "mysql" or "mariadb" or "tidb" => ConnectionType.MySql,
                    "sqlserver" or "mssql" => ConnectionType.SqlServer,
                    "sqlite" => ConnectionType.Sqlite,
                    "duckdb" => ConnectionType.DuckDb,
                    "clickhouse" => ConnectionType.ClickHouse,
                    _ => ConnectionType.Other
                };

                Guid? targetFolderId = null;
                if (!string.IsNullOrEmpty(oldFolderId) && folderMap.TryGetValue(oldFolderId, out var fId))
                {
                    targetFolderId = fId;
                }

                string? encPass = null;
                if (!string.IsNullOrEmpty(pass))
                {
                    try { encPass = vault.Encrypt(pass); }
                    catch { encPass = pass; }
                }

                targetDb.SavedConnections.Add(new SavedConnectionEntity
                {
                    Name = name,
                    ConnectionType = connType,
                    Host = host,
                    Port = port,
                    Username = user,
                    EncryptedPassword = encPass,
                    DefaultDatabase = dbName,
                    LabelColor = color,
                    FolderId = targetFolderId,
                    DatabasePath = connType == ConnectionType.Sqlite ? dbName : null
                });
                result.ImportedConnections++;
            }
            await targetDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Warning during connection profile import: {ex.Message}");
        }

        // 3. Import favorite queries
        try
        {
            using var cmd = srcConn.CreateCommand();
            cmd.CommandText = "SELECT title, text FROM favorite_query";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var title = reader["title"]?.ToString() ?? "Query";
                var queryText = reader["text"]?.ToString() ?? "";

                targetDb.FavoriteQueries.Add(new FavoriteQueryEntity
                {
                    Title = title,
                    QueryText = queryText
                });
                result.ImportedQueries++;
            }
            await targetDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Warning during favorite query import: {ex.Message}");
        }

        return result;
    }
}
