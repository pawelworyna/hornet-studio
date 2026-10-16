using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Services;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class DataImportViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly IExportImportService _exportImportService;

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private string _targetTable = string.Empty;

    [ObservableProperty]
    private bool _hasHeader = true;

    [ObservableProperty]
    private string _delimiter = ",";

    [ObservableProperty]
    private bool _isJsonFile;

    [ObservableProperty]
    private string _importButtonText = "Start Import";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _statusMessage = "Ready to import CSV or JSON data.";

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private int _rowsImported;

    public ObservableCollection<string> AvailableTables { get; } = new();
    public ObservableCollection<string> PreviewColumns { get; } = new();
    public ObservableCollection<Dictionary<string, object?>> PreviewRows { get; } = new();

    public DataImportViewModel(
        IDatabaseClient client,
        IExportImportService exportImportService,
        string? targetTable = null)
    {
        _client = client;
        _exportImportService = exportImportService;
        Title = "Import Data";
        IconKey = "IconImport";

        if (!string.IsNullOrWhiteSpace(targetTable))
        {
            TargetTable = targetTable;
        }

        _ = LoadTablesAsync();
    }

    partial void OnSourceFilePathChanged(string value)
    {
        IsJsonFile = Path.GetExtension(value).Equals(".json", StringComparison.OrdinalIgnoreCase);
        ImportButtonText = IsJsonFile ? "🚀 Start JSON Import" : "🚀 Start CSV Import";
        Title = IsJsonFile ? "Import Data (JSON)" : "Import Data (CSV)";
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var tables = await _client.ListTablesAsync();
            AvailableTables.Clear();
            foreach (var t in tables)
            {
                AvailableTables.Add(t.Name);
            }

            if (string.IsNullOrWhiteSpace(TargetTable) && AvailableTables.Count > 0)
            {
                TargetTable = AvailableTables[0];
            }
        }
        catch
        {
            // Ignore
        }
    }

    [RelayCommand]
    public async Task LoadPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFilePath) || !File.Exists(SourceFilePath))
        {
            StatusMessage = "File does not exist.";
            return;
        }

        try
        {
            if (IsJsonFile)
            {
                var preview = await _exportImportService.PreviewJsonAsync(SourceFilePath, maxRows: 10);
                PreviewColumns.Clear();
                foreach (var h in preview.Headers)
                {
                    PreviewColumns.Add(h);
                }

                PreviewRows.Clear();
                foreach (var r in preview.Rows)
                {
                    PreviewRows.Add(r);
                }

                StatusMessage = $"JSON Preview loaded: {PreviewColumns.Count} columns, showing {PreviewRows.Count} preview rows.";
            }
            else
            {
                var delim = string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
                var preview = await _exportImportService.PreviewCsvAsync(SourceFilePath, maxRows: 10, delim);
                PreviewColumns.Clear();
                foreach (var h in preview.Headers)
                {
                    PreviewColumns.Add(h);
                }

                PreviewRows.Clear();
                foreach (var r in preview.Rows)
                {
                    PreviewRows.Add(r);
                }

                StatusMessage = $"CSV Preview loaded: {PreviewColumns.Count} columns, showing {PreviewRows.Count} preview rows.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to preview: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExecuteImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFilePath) || !File.Exists(SourceFilePath))
        {
            StatusMessage = "Please select a valid data file (CSV or JSON).";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetTable))
        {
            StatusMessage = "Please select a target table.";
            return;
        }

        IsImporting = true;
        StatusMessage = "Importing data...";
        IsSuccess = false;

        try
        {
            if (IsJsonFile)
            {
                RowsImported = await _exportImportService.ImportJsonAsync(_client, TargetTable, null, SourceFilePath);
                IsSuccess = true;
                StatusMessage = $"✓ Successfully imported {RowsImported} rows from JSON into table '{TargetTable}'.";
            }
            else
            {
                var delim = string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
                RowsImported = await _exportImportService.ImportCsvAsync(_client, TargetTable, null, SourceFilePath, delim, HasHeader);
                IsSuccess = true;
                StatusMessage = $"✓ Successfully imported {RowsImported} rows from CSV into table '{TargetTable}'.";
            }
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"✕ Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }
}
