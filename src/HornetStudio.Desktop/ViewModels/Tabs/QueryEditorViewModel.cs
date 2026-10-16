using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class QueryEditorViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly HornetDbContext _dbContext;
    private readonly IExportImportService _exportImportService;
    private readonly ISqlFormatterService _sqlFormatter;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _queryText = "SELECT 1 AS status, 'Hello HornetStudio' AS message;";

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDryRun;

    [ObservableProperty]
    private bool _autoCommit = true;

    [ObservableProperty]
    private QueryResult? _currentResult;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private long _executionTimeMs;

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private string? _notificationMessage;

    public ObservableCollection<QueryResultColumn> Columns { get; } = new();
    public ObservableCollection<Dictionary<string, object?>> Rows { get; } = new();

    public event Action? ResultUpdated;
    public event Action<QueryEditorViewModel, string>? QueryDraftChanged;

    partial void OnQueryTextChanged(string value)
    {
        QueryDraftChanged?.Invoke(this, value);
    }

    public QueryEditorViewModel(
        IDatabaseClient client,
        HornetDbContext dbContext,
        IExportImportService exportImportService,
        string? initialQuery = null,
        string title = "SQL Query",
        ISqlFormatterService? sqlFormatter = null)
    {
        _client = client;
        _dbContext = dbContext;
        _exportImportService = exportImportService;
        _sqlFormatter = sqlFormatter ?? new SqlFormatterService();
        Title = title;
        IconKey = "IconPlay";

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            QueryText = initialQuery;
        }
    }

    [RelayCommand]
    public void FormatQuery()
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;
        var formatted = _sqlFormatter.Format(QueryText);
        if (!string.Equals(QueryText, formatted, StringComparison.Ordinal))
        {
            QueryText = formatted;
            StatusText = "✨ SQL formatted";
        }
    }

    [RelayCommand]
    public async Task ExecuteQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(QueryText) || IsExecuting)
            return;

        IsExecuting = true;
        ErrorMessage = null;
        NotificationMessage = null;
        StatusText = "Executing query...";
        _cts = new CancellationTokenSource();

        var sw = Stopwatch.StartNew();
        try
        {
            if (IsDryRun)
            {
                StatusText = "[DRY-RUN] Executing in dry-run mode (isolated transaction)...";
                await _client.ExecuteQueryAsync("BEGIN TRANSACTION;", _cts.Token);
                try
                {
                    var dryResult = await _client.ExecuteQueryAsync(QueryText, _cts.Token);
                    await _client.ExecuteQueryAsync("ROLLBACK;", _cts.Token);

                    CurrentResult = dryResult;
                    UpdateGridData(dryResult);
                    StatusText = $"[DRY-RUN COMPLETE] Rolled back successfully in {sw.ElapsedMilliseconds} ms. ({dryResult.Rows.Count} rows simulated)";
                }
                catch
                {
                    try { await _client.ExecuteQueryAsync("ROLLBACK;", CancellationToken.None); } catch { }
                    throw;
                }
            }
            else
            {
                var result = await _client.ExecuteQueryAsync(QueryText, _cts.Token);
                sw.Stop();
                ExecutionTimeMs = sw.ElapsedMilliseconds;
                RowCount = result.Rows.Count;
                CurrentResult = result;
                UpdateGridData(result);
                StatusText = $"Query executed in {ExecutionTimeMs} ms. {RowCount} rows returned.";

                // Log to Query History in background
                _ = LogQueryHistoryAsync(QueryText, ExecutionTimeMs, RowCount, true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Query cancelled by user.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            ErrorMessage = ex.Message;
            StatusText = $"Error ({sw.ElapsedMilliseconds} ms): {ex.Message}";
            _ = LogQueryHistoryAsync(QueryText, sw.ElapsedMilliseconds, 0, false, ex.Message);
        }
        finally
        {
            IsExecuting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void CancelExecution()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    public async Task ExplainQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(QueryText) || IsExecuting)
            return;

        IsExecuting = true;
        ErrorMessage = null;
        StatusText = "Analyzing query execution plan (EXPLAIN)...";

        var sw = Stopwatch.StartNew();
        _cts = new CancellationTokenSource();

        try
        {
            var result = await _client.ExplainQueryAsync(QueryText, analyze: false, _cts.Token);
            sw.Stop();
            ExecutionTimeMs = sw.ElapsedMilliseconds;
            RowCount = result.Rows.Count;
            CurrentResult = result;

            if (result.IsSuccess)
            {
                UpdateGridData(result);
                StatusText = $"Explain Plan generated in {ExecutionTimeMs} ms ({RowCount} plan rows).";
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to generate explain plan.";
                StatusText = $"Explain Plan failed: {ErrorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Explain Plan cancelled by user.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            ErrorMessage = ex.Message;
            StatusText = $"Explain Plan error: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public async Task SaveQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(QueryText))
            return;

        try
        {
            var favorite = new FavoriteQueryEntity
            {
                Id = Guid.NewGuid(),
                Title = Title.StartsWith("SQL Query") ? $"Query {DateTime.Now:yyyy-MM-dd HH:mm}" : Title,
                QueryText = QueryText,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.FavoriteQueries.Add(favorite);
            await _dbContext.SaveChangesAsync();
            NotificationMessage = "Query saved to Favorites!";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save favorite: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportToCsvAsync(string targetFilePath)
    {
        if (Columns.Count == 0 || Rows.Count == 0)
        {
            NotificationMessage = "No data to export.";
            return;
        }

        try
        {
            await _exportImportService.ExportToCsvAsync(Columns.ToList(), Rows.ToList(), targetFilePath);
            NotificationMessage = $"Exported to CSV: {Path.GetFileName(targetFilePath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportToJsonAsync(string targetFilePath)
    {
        if (Rows.Count == 0)
        {
            NotificationMessage = "No data to export.";
            return;
        }

        try
        {
            await _exportImportService.ExportToJsonAsync(Rows.ToList(), targetFilePath);
            NotificationMessage = $"Exported to JSON: {Path.GetFileName(targetFilePath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportToSqlAsync(string targetFilePath)
    {
        if (Columns.Count == 0 || Rows.Count == 0)
        {
            NotificationMessage = "No data to export.";
            return;
        }

        try
        {
            var targetTable = "query_results";
            await _exportImportService.ExportToSqlAsync(Columns.ToList(), Rows.ToList(), targetTable, _client.WrapIdentifier, targetFilePath);
            NotificationMessage = $"Exported to SQL: {Path.GetFileName(targetFilePath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportToExcelAsync(string targetFilePath)
    {
        if (Columns.Count == 0 || Rows.Count == 0)
        {
            NotificationMessage = "No data to export.";
            return;
        }

        try
        {
            await _exportImportService.ExportToExcelAsync(Columns.ToList(), Rows.ToList(), targetFilePath);
            NotificationMessage = $"Exported to Excel: {Path.GetFileName(targetFilePath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    private void UpdateGridData(QueryResult result)
    {
        Columns.Clear();
        foreach (var col in result.Columns)
        {
            Columns.Add(col);
        }

        Rows.Clear();
        foreach (var rawRow in result.Rows)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < result.Columns.Count && i < rawRow.Length; i++)
            {
                dict[result.Columns[i].Name] = rawRow[i];
            }
            Rows.Add(dict);
        }

        ResultUpdated?.Invoke();
    }

    private async Task LogQueryHistoryAsync(string query, long durationMs, int rows, bool success, string? error = null)
    {
        try
        {
            var history = new QueryHistoryEntity
            {
                Id = Guid.NewGuid(),
                QueryText = query,
                DurationMs = durationMs,
                AffectedRows = rows,
                Status = success ? "Success" : "Error",
                ErrorMessage = error,
                ExecutedAt = DateTime.UtcNow
            };
            _dbContext.QueryHistory.Add(history);
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Suppress logging error so it doesn't affect user query
        }
    }

    public override void OnClosed()
    {
        _cts?.Cancel();
        base.OnClosed();
    }
}
