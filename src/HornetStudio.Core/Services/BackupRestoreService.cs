using System.Globalization;
using System.Text;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace HornetStudio.Core.Services;

public enum DatabaseExportMode
{
    StructureAndData,
    StructureOnly,
    DataOnly
}

public enum DatabaseExportFormat
{
    SqlDump,
    BinaryDb // Only for SQLite
}

public class DatabaseExportOptions
{
    public DatabaseExportMode Mode { get; set; } = DatabaseExportMode.StructureAndData;
    public DatabaseExportFormat Format { get; set; } = DatabaseExportFormat.SqlDump;
    public bool IncludeDropStatements { get; set; } = true;
    public bool IncludeViews { get; set; } = true;
    public bool IncludeIndexes { get; set; } = true;
    public List<string>? SelectedTables { get; set; }
    public IProgress<string>? Progress { get; set; }
}

public interface IBackupRestoreService
{
    Task BackupAsync(IDatabaseClient client, string destinationFilePath, CancellationToken ct = default);
    Task ExportDatabaseAsync(IDatabaseClient client, string destinationFilePath, DatabaseExportOptions options, CancellationToken ct = default);
    Task RestoreAsync(IDatabaseClient client, string sourceFilePath, CancellationToken ct = default);
}

public class BackupRestoreService : IBackupRestoreService
{
    public Task BackupAsync(IDatabaseClient client, string destinationFilePath, CancellationToken ct = default)
    {
        var options = new DatabaseExportOptions
        {
            Mode = DatabaseExportMode.StructureAndData,
            Format = (client.ConnectionType == ConnectionType.Sqlite && destinationFilePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                ? DatabaseExportFormat.BinaryDb
                : DatabaseExportFormat.SqlDump
        };
        return ExportDatabaseAsync(client, destinationFilePath, options, ct);
    }

    public async Task ExportDatabaseAsync(
        IDatabaseClient client,
        string destinationFilePath,
        DatabaseExportOptions options,
        CancellationToken ct = default)
    {
        // 1. Opcja binary clone dla SQLite
        if (options.Format == DatabaseExportFormat.BinaryDb && client.ConnectionType == ConnectionType.Sqlite)
        {
            options.Progress?.Report("Performing live binary SQLite database clone...");
            var srcPath = client.Config.DatabasePath ?? ":memory:";
            using var srcConn = new SqliteConnection($"Data Source={srcPath}");
            await srcConn.OpenAsync(ct);

            using var destConn = new SqliteConnection($"Data Source={destinationFilePath}");
            await destConn.OpenAsync(ct);

            srcConn.BackupDatabase(destConn);
            options.Progress?.Report("Binary SQLite backup complete.");
            return;
        }

        // 2. Eksport pełnego skryptu SQL Dump (DDL + DML)
        options.Progress?.Report("Listing database tables...");
        var allTables = await client.ListTablesAsync(null, ct);
        var tablesToExport = options.SelectedTables != null && options.SelectedTables.Count > 0
            ? allTables.Where(t => options.SelectedTables.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList()
            : allTables.ToList();

        using var fileStream = File.Create(destinationFilePath);
        using var writer = new StreamWriter(fileStream, Encoding.UTF8);

        // Nagłówek pliku zrzutu
        await writer.WriteLineAsync($"-- ====================================================================".AsMemory(), ct);
        await writer.WriteLineAsync($"-- HornetStudio Database Export / SQL Dump".AsMemory(), ct);
        await writer.WriteLineAsync($"-- Connection: {client.Config.Name} ({client.ConnectionType})".AsMemory(), ct);
        await writer.WriteLineAsync($"-- Export Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC".AsMemory(), ct);
        await writer.WriteLineAsync($"-- Mode: {options.Mode}".AsMemory(), ct);
        await writer.WriteLineAsync($"-- ====================================================================".AsMemory(), ct);
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("BEGIN TRANSACTION;".AsMemory(), ct);
        await writer.WriteLineAsync();

        int processed = 0;
        foreach (var table in tablesToExport)
        {
            processed++;
            var fullTable = string.IsNullOrEmpty(table.Schema)
                ? client.WrapIdentifier(table.Name)
                : $"{client.WrapIdentifier(table.Schema)}.{client.WrapIdentifier(table.Name)}";

            options.Progress?.Report($"Exporting table [{processed}/{tablesToExport.Count}]: {table.Name}...");

            // A. DDL: Tworzenie tabeli
            if (options.Mode != DatabaseExportMode.DataOnly)
            {
                await writer.WriteLineAsync($"-- Structure for table {fullTable}".AsMemory(), ct);
                if (options.IncludeDropStatements)
                {
                    await writer.WriteLineAsync($"DROP TABLE IF EXISTS {fullTable};".AsMemory(), ct);
                }

                try
                {
                    var ddl = await client.GenerateCreateTableSqlAsync(table.Name, table.Schema, ct);
                    if (!string.IsNullOrWhiteSpace(ddl))
                    {
                        var trimmedDdl = ddl.Trim();
                        if (!trimmedDdl.EndsWith(';')) trimmedDdl += ";";
                        await writer.WriteLineAsync(trimmedDdl.AsMemory(), ct);
                    }
                }
                catch (Exception ex)
                {
                    await writer.WriteLineAsync($"-- Warning: Failed to generate DDL for {table.Name}: {ex.Message}".AsMemory(), ct);
                }
                await writer.WriteLineAsync();
            }

            // B. DML: Dane tabeli (INSERT)
            if (options.Mode != DatabaseExportMode.StructureOnly)
            {
                var queryResult = await client.ExecuteQueryAsync($"SELECT * FROM {fullTable}", ct);
                if (queryResult.IsSuccess && queryResult.Rows.Count > 0)
                {
                    var cleanIndices = new List<int>();
                    var cleanCols = new List<string>();
                    for (int c = 0; c < queryResult.Columns.Count; c++)
                    {
                        var colName = queryResult.Columns[c].Name;
                        if (!colName.StartsWith("_hornet_"))
                        {
                            cleanIndices.Add(c);
                            cleanCols.Add(client.WrapIdentifier(colName));
                        }
                    }

                    var colList = string.Join(", ", cleanCols);
                    await writer.WriteLineAsync($"-- Data for table {fullTable} ({queryResult.Rows.Count} rows)".AsMemory(), ct);

                    foreach (var row in queryResult.Rows)
                    {
                        var vals = cleanIndices.Select(idx => FormatSqlLiteral(row[idx]));
                        var insertSql = $"INSERT INTO {fullTable} ({colList}) VALUES ({string.Join(", ", vals)});";
                        await writer.WriteLineAsync(insertSql.AsMemory(), ct);
                    }
                    await writer.WriteLineAsync();
                }
            }

            // C. Indeksy tabeli
            if (options.IncludeIndexes && options.Mode != DatabaseExportMode.DataOnly)
            {
                try
                {
                    var indexes = await client.ListTableIndexesAsync(table.Name, table.Schema, ct);
                    var nonPkIndexes = indexes.Where(i => !i.IsPrimaryKey && !string.IsNullOrEmpty(i.Name) && i.Columns.Count > 0).ToList();

                    if (nonPkIndexes.Count > 0)
                    {
                        await writer.WriteLineAsync($"-- Indexes for {fullTable}".AsMemory(), ct);
                        foreach (var idx in nonPkIndexes)
                        {
                            var uniqueStr = idx.IsUnique ? "UNIQUE " : "";
                            var cols = string.Join(", ", idx.Columns.Select(client.WrapIdentifier));
                            var idxSql = $"CREATE {uniqueStr}INDEX IF NOT EXISTS {client.WrapIdentifier(idx.Name)} ON {fullTable} ({cols});";
                            await writer.WriteLineAsync(idxSql.AsMemory(), ct);
                        }
                        await writer.WriteLineAsync();
                    }
                }
                catch
                {
                    // Ignoruj błędy pobierania indeksów jeśli nieobsługiwane
                }
            }
        }

        // 3. Widoki (Views)
        if (options.IncludeViews && options.Mode != DatabaseExportMode.DataOnly)
        {
            try
            {
                var views = await client.ListViewsAsync(null, ct);
                if (views.Count > 0)
                {
                    await writer.WriteLineAsync("-- ====================================================================".AsMemory(), ct);
                    await writer.WriteLineAsync("-- Database Views".AsMemory(), ct);
                    await writer.WriteLineAsync("-- ====================================================================".AsMemory(), ct);

                    foreach (var view in views)
                    {
                        var fullView = string.IsNullOrEmpty(view.Schema)
                            ? client.WrapIdentifier(view.Name)
                            : $"{client.WrapIdentifier(view.Schema)}.{client.WrapIdentifier(view.Name)}";

                        if (options.IncludeDropStatements)
                        {
                            await writer.WriteLineAsync($"DROP VIEW IF EXISTS {fullView};".AsMemory(), ct);
                        }

                        var viewDdl = await client.GenerateCreateTableSqlAsync(view.Name, view.Schema, ct);
                        if (!string.IsNullOrWhiteSpace(viewDdl))
                        {
                            var trimmed = viewDdl.Trim();
                            if (!trimmed.EndsWith(';')) trimmed += ";";
                            await writer.WriteLineAsync(trimmed.AsMemory(), ct);
                        }
                    }
                    await writer.WriteLineAsync();
                }
            }
            catch
            {
                // Ignoruj błędy zrzucania widoków
            }
        }

        // Zakończenie transakcji
        await writer.WriteLineAsync("COMMIT;".AsMemory(), ct);
        await writer.WriteLineAsync($"-- ====================================================================".AsMemory(), ct);
        await writer.WriteLineAsync($"-- HornetStudio Export Completed Successfully ({tablesToExport.Count} tables)".AsMemory(), ct);
        await writer.WriteLineAsync($"-- ====================================================================".AsMemory(), ct);

        options.Progress?.Report($"Export complete. {tablesToExport.Count} tables successfully exported.");
    }

    public async Task RestoreAsync(IDatabaseClient client, string sourceFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Plik kopii nie istnieje: {sourceFilePath}");

        if (client.ConnectionType == ConnectionType.Sqlite && sourceFilePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            var destPath = client.Config.DatabasePath ?? ":memory:";
            using var srcConn = new SqliteConnection($"Data Source={sourceFilePath}");
            await srcConn.OpenAsync(ct);

            using var destConn = new SqliteConnection($"Data Source={destPath}");
            await destConn.OpenAsync(ct);

            srcConn.BackupDatabase(destConn);
            return;
        }

        var sql = await File.ReadAllTextAsync(sourceFilePath, Encoding.UTF8, ct);
        var result = await client.ExecuteQueryAsync(sql, ct);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Błąd przywracania bazy: {result.ErrorMessage}");
        }
    }

    private static string FormatSqlLiteral(object? value)
    {
        if (value == null || value is DBNull) return "NULL";
        if (value is bool b) return b ? "1" : "0";
        if (value is int or long or short or byte or float or double or decimal)
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        if (value is DateTime dt)
            return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
        if (value is DateTimeOffset dto)
            return $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'";

        var str = value.ToString() ?? "";
        return $"'{str.Replace("'", "''")}'";
    }
}
