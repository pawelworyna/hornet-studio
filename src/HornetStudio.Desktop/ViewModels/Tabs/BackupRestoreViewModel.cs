using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Services;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class TableSelectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _tableName = string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;

    public TableSelectionItemViewModel(string tableName, bool isSelected = true)
    {
        _tableName = tableName;
        _isSelected = isSelected;
    }
}

public sealed partial class BackupRestoreViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly IBackupRestoreService _backupRestoreService;

    [ObservableProperty]
    private DatabaseExportMode _exportMode = DatabaseExportMode.StructureAndData;

    [ObservableProperty]
    private DatabaseExportFormat _exportFormat = DatabaseExportFormat.SqlDump;

    public bool IsStructureAndDataMode
    {
        get => ExportMode == DatabaseExportMode.StructureAndData;
        set { if (value) { ExportMode = DatabaseExportMode.StructureAndData; OnPropertyChanged(); } }
    }

    public bool IsStructureOnlyMode
    {
        get => ExportMode == DatabaseExportMode.StructureOnly;
        set { if (value) { ExportMode = DatabaseExportMode.StructureOnly; OnPropertyChanged(); } }
    }

    public bool IsDataOnlyMode
    {
        get => ExportMode == DatabaseExportMode.DataOnly;
        set { if (value) { ExportMode = DatabaseExportMode.DataOnly; OnPropertyChanged(); } }
    }

    public bool IsSqlDumpFormat
    {
        get => ExportFormat == DatabaseExportFormat.SqlDump;
        set { if (value) { ExportFormat = DatabaseExportFormat.SqlDump; OnPropertyChanged(); } }
    }

    public bool IsBinaryDbFormat
    {
        get => ExportFormat == DatabaseExportFormat.BinaryDb;
        set { if (value) { ExportFormat = DatabaseExportFormat.BinaryDb; OnPropertyChanged(); } }
    }

    [ObservableProperty]
    private bool _includeDropTable = true;

    [ObservableProperty]
    private bool _includeViews = true;

    [ObservableProperty]
    private bool _includeIndexes = true;

    [ObservableProperty]
    private string _backupDestinationPath = string.Empty;

    [ObservableProperty]
    private string _restoreSourcePath = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready to export database.";

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    private string _selectedTablesCountSummary = string.Empty;

    public bool IsSqlite => _client.ConnectionType == ConnectionType.Sqlite;
    public string ConnectionTypeDisplay => _client.ConnectionType.ToString();

    public ObservableCollection<TableSelectionItemViewModel> AvailableTables { get; } = new();

    public BackupRestoreViewModel(
        IDatabaseClient client,
        IBackupRestoreService backupRestoreService)
    {
        _client = client;
        _backupRestoreService = backupRestoreService;
        Title = "Export Database";
        IconKey = "IconBackup";

        DatabaseName = _client.Config.DefaultDatabase ?? _client.Config.Name;
        if (string.IsNullOrWhiteSpace(DatabaseName)) DatabaseName = "database";

        var defaultName = $"{DatabaseName}_dump_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
        BackupDestinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), defaultName);

        _ = LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var tables = await _client.ListTablesAsync();
            AvailableTables.Clear();
            foreach (var t in tables)
            {
                var item = new TableSelectionItemViewModel(t.Name, isSelected: true);
                item.PropertyChanged += (s, e) => UpdateSelectedTablesSummary();
                AvailableTables.Add(item);
            }
            UpdateSelectedTablesSummary();
        }
        catch
        {
            // Ignore
        }
    }

    [RelayCommand]
    public void SelectAllTables()
    {
        foreach (var t in AvailableTables)
        {
            t.IsSelected = true;
        }
        UpdateSelectedTablesSummary();
    }

    [RelayCommand]
    public void DeselectAllTables()
    {
        foreach (var t in AvailableTables)
        {
            t.IsSelected = false;
        }
        UpdateSelectedTablesSummary();
    }

    private void UpdateSelectedTablesSummary()
    {
        var selectedCount = AvailableTables.Count(t => t.IsSelected);
        SelectedTablesCountSummary = $"Selected {selectedCount} of {AvailableTables.Count} tables";
    }

    partial void OnExportFormatChanged(DatabaseExportFormat value)
    {
        if (string.IsNullOrWhiteSpace(BackupDestinationPath)) return;

        var dir = Path.GetDirectoryName(BackupDestinationPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseName = Path.GetFileNameWithoutExtension(BackupDestinationPath);
        var newExt = value == DatabaseExportFormat.BinaryDb ? ".db" : ".sql";
        BackupDestinationPath = Path.Combine(dir, baseName + newExt);
    }

    [RelayCommand]
    public async Task RunBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupDestinationPath))
        {
            StatusMessage = "Please specify a destination file path.";
            return;
        }

        var selectedTables = AvailableTables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
        if (AvailableTables.Count > 0 && selectedTables.Count == 0 && ExportFormat == DatabaseExportFormat.SqlDump)
        {
            StatusMessage = "Please select at least one table to export.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Starting database export...";
        IsSuccess = false;

        try
        {
            var options = new DatabaseExportOptions
            {
                Mode = ExportMode,
                Format = ExportFormat,
                IncludeDropStatements = IncludeDropTable,
                IncludeViews = IncludeViews,
                IncludeIndexes = IncludeIndexes,
                SelectedTables = selectedTables,
                Progress = new Progress<string>(msg => StatusMessage = msg)
            };

            await _backupRestoreService.ExportDatabaseAsync(_client, BackupDestinationPath, options);
            IsSuccess = true;
            StatusMessage = $"✓ Database export completed successfully! Saved to: {BackupDestinationPath}";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"✕ Export failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public async Task RunRestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(RestoreSourcePath) || !File.Exists(RestoreSourcePath))
        {
            StatusMessage = "Please select a valid existing backup/dump file to restore.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Restoring database from file...";
        IsSuccess = false;

        try
        {
            await _backupRestoreService.RestoreAsync(_client, RestoreSourcePath);
            IsSuccess = true;
            StatusMessage = $"✓ Database restored successfully from: {RestoreSourcePath}";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"✕ Restore failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
