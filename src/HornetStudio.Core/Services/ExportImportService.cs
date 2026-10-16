using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Services;

public enum ExportFormat
{
    Csv,
    Json,
    JsonLines,
    Sql,
    Xlsx
}

public interface IExportService
{
    Task ExportAsync(
        IDatabaseClient client,
        string sql,
        ExportFormat format,
        string outputPath,
        string? targetTableName = null,
        CancellationToken ct = default);

    Task ExportTableAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        ExportFormat format,
        string outputPath,
        List<TableFilter>? filters = null,
        string? rawWhereClause = null,
        CancellationToken ct = default);
}

public interface IImportService
{
    Task<int> ImportCsvAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        string csvFilePath,
        char delimiter = ',',
        bool hasHeader = true,
        CancellationToken ct = default);

    Task<int> ImportJsonAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        string jsonFilePath,
        CancellationToken ct = default);
}

public interface IExportImportService : IExportService, IImportService
{
    Task ExportToCsvAsync(IReadOnlyList<QueryResultColumn> columns, IReadOnlyList<Dictionary<string, object?>> rows, string outputPath, CancellationToken ct = default);
    Task ExportToCsvAsync(IReadOnlyList<string> columnNames, IReadOnlyList<Dictionary<string, object?>> rows, string outputPath, CancellationToken ct = default);
    Task ExportToJsonAsync(IReadOnlyList<Dictionary<string, object?>> rows, string outputPath, CancellationToken ct = default);
    Task ExportToSqlAsync(IReadOnlyList<QueryResultColumn> columns, IReadOnlyList<Dictionary<string, object?>> rows, string tableName, Func<string, string> wrapId, string outputPath, CancellationToken ct = default);
    Task ExportToSqlAsync(IReadOnlyList<string> columnNames, IReadOnlyList<Dictionary<string, object?>> rows, string tableName, Func<string, string> wrapId, string outputPath, CancellationToken ct = default);
    Task ExportToExcelAsync(IReadOnlyList<QueryResultColumn> columns, IReadOnlyList<Dictionary<string, object?>> rows, string outputPath, string sheetName = "Sheet1", CancellationToken ct = default);
    Task ExportToExcelAsync(IReadOnlyList<string> columnNames, IReadOnlyList<Dictionary<string, object?>> rows, string outputPath, string sheetName = "Sheet1", CancellationToken ct = default);
    Task<(List<string> Headers, List<Dictionary<string, object?>> Rows)> PreviewCsvAsync(string csvFilePath, int maxRows = 10, char delimiter = ',');
    Task<(List<string> Headers, List<Dictionary<string, object?>> Rows)> PreviewJsonAsync(string jsonFilePath, int maxRows = 10, CancellationToken ct = default);
}

public class ExportImportService : IExportImportService
{
    public async Task ExportAsync(
        IDatabaseClient client,
        string sql,
        ExportFormat format,
        string outputPath,
        string? targetTableName = null,
        CancellationToken ct = default)
    {
        var result = await client.ExecuteQueryAsync(sql, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Błąd zapytania przy eksporcie: {result.ErrorMessage}");

        if (format == ExportFormat.Xlsx)
        {
            await ExportExcelAsync(result, outputPath, targetTableName ?? "Sheet1", ct);
            return;
        }

        using var fileStream = File.Create(outputPath);
        using var writer = new StreamWriter(fileStream, Encoding.UTF8);

        switch (format)
        {
            case ExportFormat.Csv:
                await ExportCsvAsync(result, writer, ct);
                break;
            case ExportFormat.Json:
                await ExportJsonAsync(result, writer, ct);
                break;
            case ExportFormat.JsonLines:
                await ExportJsonLinesAsync(result, writer, ct);
                break;
            case ExportFormat.Sql:
                await ExportSqlAsync(result, targetTableName ?? "exported_table", client.WrapIdentifier, writer, ct);
                break;
        }
    }

    public async Task ExportTableAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        ExportFormat format,
        string outputPath,
        List<TableFilter>? filters = null,
        string? rawWhereClause = null,
        CancellationToken ct = default)
    {
        bool hasFilter = (filters != null && filters.Count > 0) || !string.IsNullOrWhiteSpace(rawWhereClause);

        if (hasFilter)
        {
            var options = new SelectTopOptions
            {
                Table = table,
                Schema = schema,
                Limit = 1_000_000,
                Offset = 0,
                Filters = filters ?? [],
                RawWhereClause = rawWhereClause
            };

            var tableResult = await client.SelectTopAsync(options, ct);
            var colNames = tableResult.Columns.Select(c => c.ColumnName).ToList();

            switch (format)
            {
                case ExportFormat.Csv:
                    await ExportToCsvAsync(colNames, tableResult.Rows, outputPath, ct);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonAsync(tableResult.Rows, outputPath, ct);
                    break;
                case ExportFormat.JsonLines:
                    using (var fs = File.Create(outputPath))
                    using (var wr = new StreamWriter(fs, Encoding.UTF8))
                    {
                        foreach (var row in tableResult.Rows)
                        {
                            var line = JsonSerializer.Serialize(row);
                            await wr.WriteLineAsync(line.AsMemory(), ct);
                        }
                    }
                    break;
                case ExportFormat.Sql:
                    await ExportToSqlAsync(colNames, tableResult.Rows, table, client.WrapIdentifier, outputPath, ct);
                    break;
                case ExportFormat.Xlsx:
                    await ExportToExcelAsync(colNames, tableResult.Rows, outputPath, table, ct);
                    break;
            }
        }
        else
        {
            var fullTable = string.IsNullOrEmpty(schema)
                ? client.WrapIdentifier(table)
                : $"{client.WrapIdentifier(schema)}.{client.WrapIdentifier(table)}";
            await ExportAsync(client, $"SELECT * FROM {fullTable}", format, outputPath, table, ct);
        }
    }

    public Task ExportToCsvAsync(
        IReadOnlyList<QueryResultColumn> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string outputPath,
        CancellationToken ct = default)
    {
        return ExportToCsvAsync(columns.Select(c => c.Name).ToList(), rows, outputPath, ct);
    }

    public async Task ExportToCsvAsync(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string outputPath,
        CancellationToken ct = default)
    {
        using var fileStream = File.Create(outputPath);
        using var writer = new StreamWriter(fileStream, Encoding.UTF8);

        var header = string.Join(",", columnNames.Select(EscapeCsv));
        await writer.WriteLineAsync(header.AsMemory(), ct);

        foreach (var row in rows)
        {
            var line = string.Join(",", columnNames.Select(c =>
            {
                row.TryGetValue(c, out var v);
                return EscapeCsv(v?.ToString() ?? "");
            }));
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    public async Task ExportToJsonAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string outputPath,
        CancellationToken ct = default)
    {
        var cleanRows = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var (k, v) in row)
            {
                if (!k.StartsWith("_hornet_"))
                {
                    dict[k] = v;
                }
            }
            cleanRows.Add(dict);
        }

        var json = JsonSerializer.Serialize(cleanRows, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, ct);
    }

    public Task ExportToSqlAsync(
        IReadOnlyList<QueryResultColumn> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string tableName,
        Func<string, string> wrapId,
        string outputPath,
        CancellationToken ct = default)
    {
        return ExportToSqlAsync(columns.Select(c => c.Name).ToList(), rows, tableName, wrapId, outputPath, ct);
    }

    public async Task ExportToSqlAsync(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string tableName,
        Func<string, string> wrapId,
        string outputPath,
        CancellationToken ct = default)
    {
        var cleanColumns = columnNames.Where(c => !c.StartsWith("_hornet_")).ToList();
        var colList = string.Join(", ", cleanColumns.Select(wrapId));

        using var fileStream = File.Create(outputPath);
        using var writer = new StreamWriter(fileStream, Encoding.UTF8);

        await writer.WriteLineAsync($"-- HornetStudio SQL Export - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC".AsMemory(), ct);
        await writer.WriteLineAsync("BEGIN TRANSACTION;".AsMemory(), ct);

        foreach (var row in rows)
        {
            var vals = cleanColumns.Select(col =>
            {
                row.TryGetValue(col, out var v);
                return FormatSqlLiteral(v);
            });
            var insertSql = $"INSERT INTO {wrapId(tableName)} ({colList}) VALUES ({string.Join(", ", vals)});";
            await writer.WriteLineAsync(insertSql.AsMemory(), ct);
        }

        await writer.WriteLineAsync("COMMIT;".AsMemory(), ct);
    }

    public Task ExportToExcelAsync(
        IReadOnlyList<QueryResultColumn> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string outputPath,
        string sheetName = "Sheet1",
        CancellationToken ct = default)
    {
        return ExportToExcelAsync(columns.Select(c => c.Name).ToList(), rows, outputPath, sheetName, ct);
    }

    public async Task ExportToExcelAsync(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string outputPath,
        string sheetName = "Sheet1",
        CancellationToken ct = default)
    {
        var cleanColumns = columnNames.Where(c => !c.StartsWith("_hornet_")).ToList();
        var rowValues = rows.Select(r => (IReadOnlyList<object?>)cleanColumns.Select(c => r.TryGetValue(c, out var v) ? v : null).ToList()).ToList();

        await CreateExcelPackageAsync(outputPath, cleanColumns, rowValues, sheetName, ct);
    }

    public async Task<(List<string> Headers, List<Dictionary<string, object?>> Rows)> PreviewCsvAsync(
        string csvFilePath,
        int maxRows = 10,
        char delimiter = ',')
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"Plik CSV nie istnieje: {csvFilePath}");

        var lines = await File.ReadAllLinesAsync(csvFilePath, Encoding.UTF8);
        if (lines.Length == 0) return ([], []);

        var headers = ParseCsvLine(lines[0], delimiter);
        var previewRows = new List<Dictionary<string, object?>>();

        for (int i = 1; i < lines.Length && previewRows.Count < maxRows; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line, delimiter);
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (int h = 0; h < headers.Count && h < fields.Count; h++)
            {
                row[headers[h]] = fields[h];
            }
            previewRows.Add(row);
        }

        return (headers, previewRows);
    }

    public async Task<(List<string> Headers, List<Dictionary<string, object?>> Rows)> PreviewJsonAsync(
        string jsonFilePath,
        int maxRows = 10,
        CancellationToken ct = default)
    {
        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"Plik JSON nie istnieje: {jsonFilePath}");

        var rows = await ParseJsonRowsAsync(jsonFilePath, maxRows, ct);
        var headerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headers = new List<string>();

        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (headerSet.Add(key))
                {
                    headers.Add(key);
                }
            }
        }

        return (headers, rows);
    }

    public async Task<int> ImportJsonAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        string jsonFilePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"Plik JSON nie istnieje: {jsonFilePath}");

        var rows = await ParseJsonRowsAsync(jsonFilePath, null, ct);
        if (rows.Count == 0) return 0;

        var dbCols = await client.ListTableColumnsAsync(table, schema, ct);
        var validColNames = new HashSet<string>(dbCols.Select(c => c.ColumnName), StringComparer.OrdinalIgnoreCase);

        int totalAffected = 0;
        const int batchSize = 250;

        for (int batchStart = 0; batchStart < rows.Count; batchStart += batchSize)
        {
            var batchRows = rows.Skip(batchStart).Take(batchSize);
            var changes = new TableChanges
            {
                Table = table,
                Schema = schema
            };

            foreach (var row in batchRows)
            {
                var insert = new TableInsert();
                foreach (var (k, v) in row)
                {
                    if (validColNames.Count > 0 && !validColNames.Contains(k))
                        continue;

                    insert.Values[k] = v;
                }

                if (insert.Values.Count > 0)
                {
                    changes.Inserts.Add(insert);
                }
            }

            if (changes.Inserts.Count > 0)
            {
                var res = await client.ApplyChangesAsync(changes, ct);
                if (!res.Success)
                {
                    throw new InvalidOperationException($"Błąd importu danych JSON: {res.ErrorMessage}");
                }
                totalAffected += res.AffectedRows;
            }
        }

        return totalAffected;
    }

    public async Task<int> ImportCsvAsync(
        IDatabaseClient client,
        string table,
        string? schema,
        string csvFilePath,
        char delimiter = ',',
        bool hasHeader = true,
        CancellationToken ct = default)
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"Plik CSV nie istnieje: {csvFilePath}");

        var lines = await File.ReadAllLinesAsync(csvFilePath, Encoding.UTF8, ct);
        if (lines.Length == 0) return 0;

        var startIdx = 0;
        List<string> headers;

        if (hasHeader)
        {
            headers = ParseCsvLine(lines[0], delimiter);
            startIdx = 1;
        }
        else
        {
            var columns = await client.ListTableColumnsAsync(table, schema, ct);
            headers = columns.Select(c => c.ColumnName).ToList();
        }

        var dbCols = await client.ListTableColumnsAsync(table, schema, ct);
        var validColNames = new HashSet<string>(dbCols.Select(c => c.ColumnName), StringComparer.OrdinalIgnoreCase);

        int totalAffected = 0;
        const int batchSize = 250;

        for (int batchStart = startIdx; batchStart < lines.Length; batchStart += batchSize)
        {
            var changes = new TableChanges
            {
                Table = table,
                Schema = schema
            };

            for (int i = batchStart; i < Math.Min(batchStart + batchSize, lines.Length); i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var fields = ParseCsvLine(line, delimiter);
                var insert = new TableInsert();

                for (int h = 0; h < headers.Count && h < fields.Count; h++)
                {
                    var colName = headers[h];
                    if (validColNames.Count > 0 && !validColNames.Contains(colName))
                        continue;

                    insert.Values[colName] = fields[h];
                }

                if (insert.Values.Count > 0)
                {
                    changes.Inserts.Add(insert);
                }
            }

            if (changes.Inserts.Count > 0)
            {
                var updateResult = await client.ApplyChangesAsync(changes, ct);
                if (!updateResult.Success)
                {
                    throw new InvalidOperationException($"Błąd importu CSV: {updateResult.ErrorMessage}");
                }
                totalAffected += updateResult.AffectedRows;
            }
        }

        return totalAffected;
    }

    private static async Task<List<Dictionary<string, object?>>> ParseJsonRowsAsync(
        string jsonFilePath,
        int? maxRows = null,
        CancellationToken ct = default)
    {
        var jsonText = await File.ReadAllTextAsync(jsonFilePath, Encoding.UTF8, ct);
        if (string.IsNullOrWhiteSpace(jsonText))
            return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException)
        {
            return ParseJsonLines(jsonText, maxRows);
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement arrayElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                bool foundArray = false;
                arrayElement = default;
                foreach (var propName in new[] { "data", "rows", "records", "items", "values", "results" })
                {
                    if (root.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Array)
                    {
                        arrayElement = prop;
                        foundArray = true;
                        break;
                    }
                }

                if (!foundArray)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            arrayElement = prop.Value;
                            foundArray = true;
                            break;
                        }
                    }
                }

                if (!foundArray)
                {
                    var singleRow = JsonElementToDictionary(root);
                    return [singleRow];
                }
            }
            else
            {
                return [];
            }

            var result = new List<Dictionary<string, object?>>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    result.Add(JsonElementToDictionary(item));
                    if (maxRows.HasValue && result.Count >= maxRows.Value)
                        break;
                }
            }
            return result;
        }
    }

    private static List<Dictionary<string, object?>> ParseJsonLines(string text, int? maxRows)
    {
        var list = new List<Dictionary<string, object?>>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    list.Add(JsonElementToDictionary(doc.RootElement));
                    if (maxRows.HasValue && list.Count >= maxRows.Value)
                        break;
                }
            }
            catch
            {
                // Skip invalid JSON lines
            }
        }
        return list;
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonValue(prop.Value);
        }
        return dict;
    }

    private static object? ConvertJsonValue(JsonElement val)
    {
        return val.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => val.TryGetInt64(out var l)
                ? (object)l
                : (val.TryGetDouble(out var d) ? (object)d : val.GetRawText()),
            JsonValueKind.String => val.GetString(),
            JsonValueKind.Array or JsonValueKind.Object => val.GetRawText(),
            _ => val.ToString()
        };
    }

    private static async Task ExportCsvAsync(QueryResult result, StreamWriter writer, CancellationToken ct)
    {
        var header = string.Join(",", result.Columns.Select(c => EscapeCsv(c.Name)));
        await writer.WriteLineAsync(header.AsMemory(), ct);

        foreach (var row in result.Rows)
        {
            var line = string.Join(",", row.Select(v => EscapeCsv(v?.ToString() ?? "")));
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    private static async Task ExportJsonAsync(QueryResult result, StreamWriter writer, CancellationToken ct)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var row in result.Rows)
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                var colName = result.Columns[i].Name;
                if (!colName.StartsWith("_hornet_"))
                {
                    dict[colName] = row[i];
                }
            }
            list.Add(dict);
        }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await writer.WriteAsync(json.AsMemory(), ct);
    }

    private static async Task ExportJsonLinesAsync(QueryResult result, StreamWriter writer, CancellationToken ct)
    {
        foreach (var row in result.Rows)
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                var colName = result.Columns[i].Name;
                if (!colName.StartsWith("_hornet_"))
                {
                    dict[colName] = row[i];
                }
            }
            var line = JsonSerializer.Serialize(dict);
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    private static async Task ExportSqlAsync(
        QueryResult result,
        string tableName,
        Func<string, string> wrapId,
        StreamWriter writer,
        CancellationToken ct)
    {
        var cleanIndices = new List<int>();
        var cleanCols = new List<string>();
        for (int i = 0; i < result.Columns.Count; i++)
        {
            if (!result.Columns[i].Name.StartsWith("_hornet_"))
            {
                cleanIndices.Add(i);
                cleanCols.Add(wrapId(result.Columns[i].Name));
            }
        }

        var colList = string.Join(", ", cleanCols);

        await writer.WriteLineAsync($"-- HornetStudio SQL Export - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC".AsMemory(), ct);
        await writer.WriteLineAsync("BEGIN TRANSACTION;".AsMemory(), ct);

        foreach (var row in result.Rows)
        {
            var vals = cleanIndices.Select(i => FormatSqlLiteral(row[i]));
            var insertSql = $"INSERT INTO {wrapId(tableName)} ({colList}) VALUES ({string.Join(", ", vals)});";
            await writer.WriteLineAsync(insertSql.AsMemory(), ct);
        }

        await writer.WriteLineAsync("COMMIT;".AsMemory(), ct);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
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

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static async Task ExportExcelAsync(
        QueryResult result,
        string outputPath,
        string sheetName = "Sheet1",
        CancellationToken ct = default)
    {
        var cleanIndices = new List<int>();
        var cleanColumns = new List<string>();

        for (int i = 0; i < result.Columns.Count; i++)
        {
            if (!result.Columns[i].Name.StartsWith("_hornet_"))
            {
                cleanIndices.Add(i);
                cleanColumns.Add(result.Columns[i].Name);
            }
        }

        var rowValues = result.Rows.Select(r => (IReadOnlyList<object?>)cleanIndices.Select(i => r[i]).ToList()).ToList();

        await CreateExcelPackageAsync(outputPath, cleanColumns, rowValues, sheetName, ct);
    }

    private static async Task CreateExcelPackageAsync(
        string outputPath,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        string sheetName,
        CancellationToken ct)
    {
        var cleanSheetName = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName.Trim();
        cleanSheetName = Regex.Replace(cleanSheetName, @"[\\/\?\*\[\]:]", "_");
        if (cleanSheetName.Length > 31) cleanSheetName = cleanSheetName[..31];

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        // 1. [Content_Types].xml
        var contentTypesEntry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Fastest);
        using (var stream = contentTypesEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(ContentTypesXml.AsMemory(), ct);
        }

        // 2. _rels/.rels
        var rootRelsEntry = archive.CreateEntry("_rels/.rels", CompressionLevel.Fastest);
        using (var stream = rootRelsEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(RootRelsXml.AsMemory(), ct);
        }

        // 3. xl/_rels/workbook.xml.rels
        var wbRelsEntry = archive.CreateEntry("xl/_rels/workbook.xml.rels", CompressionLevel.Fastest);
        using (var stream = wbRelsEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(WorkbookRelsXml.AsMemory(), ct);
        }

        // 4. xl/workbook.xml
        var wbEntry = archive.CreateEntry("xl/workbook.xml", CompressionLevel.Fastest);
        using (var stream = wbEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            var wbXml = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n  <sheets>\n    <sheet name=\"{EscapeXml(cleanSheetName)}\" sheetId=\"1\" r:id=\"rId1\"/>\n  </sheets>\n</workbook>";
            await writer.WriteAsync(wbXml.AsMemory(), ct);
        }

        // 5. xl/styles.xml
        var stylesEntry = archive.CreateEntry("xl/styles.xml", CompressionLevel.Fastest);
        using (var stream = stylesEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(StylesXml.AsMemory(), ct);
        }

        // 6. xl/worksheets/sheet1.xml
        var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using (var stream = sheetEntry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await WriteWorksheetXmlAsync(writer, columnNames, rows, ct);
        }
    }

    private static async Task WriteWorksheetXmlAsync(
        StreamWriter writer,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken ct)
    {
        await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>".AsMemory(), ct);
        await writer.WriteLineAsync("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">".AsMemory(), ct);
        await writer.WriteLineAsync("  <sheetData>".AsMemory(), ct);

        // Header Row (bold style s="1")
        await writer.WriteAsync("    <row r=\"1\">".AsMemory(), ct);
        for (int c = 0; c < columns.Count; c++)
        {
            var colLetter = GetColumnLetter(c + 1);
            var headerName = EscapeXml(columns[c]);
            await writer.WriteAsync($"<c r=\"{colLetter}1\" t=\"inlineStr\" s=\"1\"><is><t>{headerName}</t></is></c>".AsMemory(), ct);
        }
        await writer.WriteLineAsync("</row>".AsMemory(), ct);

        // Data Rows
        int rowIdx = 2;
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Clear();
            sb.Append($"    <row r=\"{rowIdx}\">");

            for (int c = 0; c < columns.Count; c++)
            {
                var colLetter = GetColumnLetter(c + 1);
                var cellRef = $"{colLetter}{rowIdx}";
                var val = c < row.Count ? row[c] : null;

                if (val == null || val is DBNull)
                {
                    continue;
                }

                if (val is bool b)
                {
                    sb.Append($"<c r=\"{cellRef}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>");
                }
                else if (IsNumeric(val))
                {
                    var numStr = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "0";
                    sb.Append($"<c r=\"{cellRef}\"><v>{numStr}</v></c>");
                }
                else if (val is DateTime dt)
                {
                    var dtStr = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(dtStr)}</t></is></c>");
                }
                else if (val is DateTimeOffset dto)
                {
                    var dtoStr = dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(dtoStr)}</t></is></c>");
                }
                else
                {
                    var text = EscapeXml(val.ToString() ?? "");
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{text}</t></is></c>");
                }
            }

            sb.Append("</row>\n");
            await writer.WriteAsync(sb.ToString().AsMemory(), ct);
            rowIdx++;
        }

        await writer.WriteLineAsync("  </sheetData>".AsMemory(), ct);
        await writer.WriteLineAsync("</worksheet>".AsMemory(), ct);
    }

    private static bool IsNumeric(object val) =>
        val is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    public static string GetColumnLetter(int colIndex)
    {
        var dividend = colIndex;
        var colName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            colName = (char)(65 + modulo) + colName;
            dividend = (dividend - modulo) / 26;
        }

        return colName;
    }

    public static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var clean = Regex.Replace(value, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
        return clean
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string RootRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="2">
            <font><sz val="11"/><name val="Segoe UI"/></font>
            <font><b/><sz val="11"/><name val="Segoe UI"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
          </fills>
          <borders count="1">
            <border><left/><right/><top/><bottom/><diagonal/></border>
          </borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="0" borderId="0" applyFont="1"/>
          </cellXfs>
        </styleSheet>
        """;
}
