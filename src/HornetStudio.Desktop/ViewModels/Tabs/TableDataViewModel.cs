using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class TableDataViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly IExportImportService _exportImportService;
    private readonly IClipboardService _clipboardService;

    public TableOrView Table { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _pageNumber = 1;

    [ObservableProperty]
    private int _pageSize = 100;

    [ObservableProperty]
    private long _totalRowCount;

    [ObservableProperty]
    private RowIdentifierMode _rowIdMode;

    [ObservableProperty]
    private bool _isNoPkMode;

    [ObservableProperty]
    private string _rowIdentifierDescription = string.Empty;

    [ObservableProperty]
    private EditableRowViewModel? _selectedRow;

    [ObservableProperty]
    private bool _hasStagedChanges;

    [ObservableProperty]
    private string _stagedChangesSummary = string.Empty;

    [ObservableProperty]
    private string? _generatedSqlPreview;

    [ObservableProperty]
    private bool _isFilterBarOpen = true;

    [ObservableProperty]
    private bool _isCellInspectorOpen;

    [ObservableProperty]
    private CellInspectorViewModel? _cellInspector;

    [ObservableProperty]
    private bool _isRawFilterMode;

    [ObservableProperty]
    private string _rawFilterText = string.Empty;

    [ObservableProperty]
    private bool _hasActiveFilter;

    [ObservableProperty]
    private string _activeFilterSummary = string.Empty;

    public ObservableCollection<string> AvailableColumnNames { get; } = new();
    public ObservableCollection<ExtendedTableColumn> Columns { get; } = new();
    public ObservableCollection<EditableRowViewModel> Rows { get; } = new();
    public ObservableCollection<TableFilterViewModel> Filters { get; } = new();

    public event Action? DataRefreshed;

    public TableDataViewModel(
        IDatabaseClient client,
        IExportImportService exportImportService,
        TableOrView table,
        IClipboardService? clipboardService = null)
    {
        _client = client;
        _exportImportService = exportImportService;
        Table = table;
        _clipboardService = clipboardService ?? new NullClipboardService();
        Title = $"{table.Name} (Data)";
        IconKey = table.IsView ? "IconView" : "IconTable";
    }

    private void UpdateRowIdentifierMode(TableResult result)
    {
        RowIdMode = result.IdentifierMode;
        IsNoPkMode = RowIdMode != RowIdentifierMode.PrimaryKey;

        RowIdentifierDescription = RowIdMode switch
        {
            RowIdentifierMode.PrimaryKey => $"Primary Key: {string.Join(", ", result.PrimaryKeys)}",
            RowIdentifierMode.RowId => "⚡ No-PK Mode: Editing enabled via SQLite rowid",
            RowIdentifierMode.Ctid => "⚡ No-PK Mode: Editing enabled via PostgreSQL ctid",
            RowIdentifierMode.AllColumns => "⚡ No-PK Mode: Editing enabled via All-Columns match",
            RowIdentifierMode.UniqueIndex => "⚡ No-PK Mode: Editing enabled via Unique Index",
            _ => "Read-only"
        };
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading table data...";

        try
        {
            var sw = Stopwatch.StartNew();

            List<TableFilter>? filterList = null;
            string? rawWhere = null;

            if (IsRawFilterMode && !string.IsNullOrWhiteSpace(RawFilterText))
            {
                rawWhere = RawFilterText;
                HasActiveFilter = true;
                ActiveFilterSummary = $"Raw: {RawFilterText}";
            }
            else if (!IsRawFilterMode && Filters.Any(f => !string.IsNullOrWhiteSpace(f.Field) && (!f.IsValueRequired || !string.IsNullOrWhiteSpace(f.Value))))
            {
                filterList = Filters
                    .Where(f => !string.IsNullOrWhiteSpace(f.Field) && (!f.IsValueRequired || !string.IsNullOrWhiteSpace(f.Value)))
                    .Select(f => f.ToDomainFilter())
                    .ToList();
                HasActiveFilter = filterList.Count > 0;
                ActiveFilterSummary = filterList.Count > 0 ? $"{filterList.Count} filter(s) active" : string.Empty;
            }
            else
            {
                HasActiveFilter = false;
                ActiveFilterSummary = string.Empty;
            }

            var options = new SelectTopOptions
            {
                Table = Table.Name,
                Schema = Table.Schema,
                Limit = PageSize,
                Offset = (PageNumber - 1) * PageSize,
                Filters = filterList ?? [],
                RawWhereClause = rawWhere
            };

            var tableResult = await _client.SelectTopAsync(options);
            sw.Stop();

            UpdateRowIdentifierMode(tableResult);
            TotalRowCount = tableResult.TotalCount;

            var newColNames = tableResult.Columns.Select(c => c.ColumnName).ToList();
            if (!AvailableColumnNames.SequenceEqual(newColNames))
            {
                Columns.Clear();
                AvailableColumnNames.Clear();
                foreach (var col in tableResult.Columns)
                {
                    Columns.Add(col);
                    AvailableColumnNames.Add(col.ColumnName);
                }

                if (Filters.Count == 0 && AvailableColumnNames.Count > 0)
                {
                    AddFilter();
                }

                DataRefreshed?.Invoke();
            }

            Rows.Clear();
            foreach (var r in tableResult.Rows)
            {
                var rowVm = new EditableRowViewModel(r, isNew: false);
                rowVm.PropertyChanged += (s, e) => UpdateStagedChangesSummary();
                Rows.Add(rowVm);
            }

            UpdateStagedChangesSummary();
            var filterSuffix = HasActiveFilter ? $" [Filtered: {ActiveFilterSummary}]" : "";
            StatusMessage = $"Loaded {Rows.Count} of {(TotalRowCount >= 0 ? TotalRowCount.ToString() : "unknown")} rows in {sw.ElapsedMilliseconds} ms.{filterSuffix}";
            DataRefreshed?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task NextPageAsync()
    {
        if (TotalRowCount < 0 || (PageNumber * PageSize) < TotalRowCount)
        {
            PageNumber++;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task PrevPageAsync()
    {
        if (PageNumber > 1)
        {
            PageNumber--;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public void AddNewRow()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in Columns)
        {
            dict[col.ColumnName] = col.DefaultValue ?? null;
        }

        var newRow = new EditableRowViewModel(dict, isNew: true);
        newRow.PropertyChanged += (s, e) => UpdateStagedChangesSummary();
        Rows.Insert(0, newRow);
        SelectedRow = newRow;
        UpdateStagedChangesSummary();
    }

    [RelayCommand]
    public void DeleteSelectedRow()
    {
        if (SelectedRow == null)
            return;

        if (SelectedRow.IsNewRow)
        {
            Rows.Remove(SelectedRow);
        }
        else
        {
            SelectedRow.MarkForDeletion(!SelectedRow.IsMarkedForDeletion);
        }

        UpdateStagedChangesSummary();
    }

    [RelayCommand]
    public void DiscardChanges()
    {
        var toRemove = Rows.Where(r => r.IsNewRow).ToList();
        foreach (var r in toRemove)
        {
            Rows.Remove(r);
        }

        foreach (var r in Rows)
        {
            r.ResetChanges();
        }

        UpdateStagedChangesSummary();
        GeneratedSqlPreview = null;
    }

    public TableChanges CollectChanges()
    {
        var changes = new TableChanges
        {
            Table = Table.Name,
            Schema = Table.Schema,
            IdentifierMode = RowIdMode
        };

        foreach (var r in Rows)
        {
            if (r.IsNewRow && !r.IsMarkedForDeletion)
            {
                var insDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in r.CurrentValues)
                {
                    if (!k.StartsWith("_hornet_"))
                        insDict[k] = v;
                }
                changes.Inserts.Add(new TableInsert { Values = insDict });
            }
            else if (r.IsMarkedForDeletion)
            {
                var del = new TableDelete { OriginalRow = new Dictionary<string, object?>(r.OriginalValues) };
                if (r.OriginalValues.TryGetValue("_hornet_rowid", out var rowidVal) && rowidVal != null)
                {
                    del.RowId = Convert.ToInt64(rowidVal);
                }
                if (r.OriginalValues.TryGetValue("_hornet_ctid", out var ctidVal) && ctidVal != null)
                {
                    del.Ctid = ctidVal.ToString();
                }
                changes.Deletes.Add(del);
            }
            else if (r.ModifiedColumns.Count > 0)
            {
                var changedValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in r.ModifiedColumns)
                {
                    changedValues[col] = r.CurrentValues[col];
                }

                var upd = new TableUpdate
                {
                    OriginalRow = new Dictionary<string, object?>(r.OriginalValues),
                    UpdatedValues = changedValues
                };

                if (r.OriginalValues.TryGetValue("_hornet_rowid", out var rowidVal) && rowidVal != null)
                {
                    upd.RowId = Convert.ToInt64(rowidVal);
                }
                if (r.OriginalValues.TryGetValue("_hornet_ctid", out var ctidVal) && ctidVal != null)
                {
                    upd.Ctid = ctidVal.ToString();
                }

                changes.Updates.Add(upd);
            }
        }

        return changes;
    }

    [RelayCommand]
    public async Task ApplyChangesAsync()
    {
        var changes = CollectChanges();
        var totalChanges = changes.Inserts.Count + changes.Updates.Count + changes.Deletes.Count;
        if (totalChanges == 0)
            return;

        IsLoading = true;
        StatusMessage = "Applying changes to database...";

        try
        {
            var result = await _client.ApplyChangesAsync(changes);
            if (result.Success)
            {
                StatusMessage = $"✓ Changes applied successfully ({result.AffectedRows} rows affected).";
                await LoadDataAsync();
            }
            else
            {
                StatusMessage = $"✕ Failed to apply changes: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✕ Error applying changes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void GenerateSqlPreview()
    {
        var changes = CollectChanges();
        var statements = new List<string>();
        var fullTable = string.IsNullOrEmpty(Table.Schema) ? _client.WrapIdentifier(Table.Name) : $"{_client.WrapIdentifier(Table.Schema)}.{_client.WrapIdentifier(Table.Name)}";

        foreach (var ins in changes.Inserts)
        {
            var cols = string.Join(", ", ins.Values.Keys.Select(_client.WrapIdentifier));
            var vals = string.Join(", ", ins.Values.Values.Select(v => v == null ? "NULL" : $"'{v.ToString()?.Replace("'", "''")}'"));
            statements.Add($"INSERT INTO {fullTable} ({cols}) VALUES ({vals});");
        }

        foreach (var upd in changes.Updates)
        {
            var sets = string.Join(", ", upd.UpdatedValues.Select(kv => $"{_client.WrapIdentifier(kv.Key)} = {(kv.Value == null ? "NULL" : $"'{kv.Value.ToString()?.Replace("'", "''")}'")}"));
            statements.Add($"UPDATE {fullTable} SET {sets};");
        }

        foreach (var del in changes.Deletes)
        {
            statements.Add($"DELETE FROM {fullTable};");
        }

        GeneratedSqlPreview = statements.Count > 0 ? string.Join(Environment.NewLine, statements) : "-- No staged changes";
    }

    [RelayCommand]
    public void ToggleFilterBar()
    {
        IsFilterBarOpen = !IsFilterBarOpen;
    }

    [RelayCommand]
    public void ToggleFilterMode()
    {
        IsRawFilterMode = !IsRawFilterMode;
    }

    [RelayCommand]
    public void AddFilter()
    {
        var firstCol = AvailableColumnNames.FirstOrDefault() ?? Columns.FirstOrDefault()?.ColumnName ?? "id";
        var filterVm = new TableFilterViewModel
        {
            Field = firstCol,
            Operator = FilterOperator.Equals,
            Value = string.Empty,
            IsFirstFilter = Filters.Count == 0,
            LogicalOp = "AND",
            AvailableColumns = AvailableColumnNames
        };

        Filters.Add(filterVm);
        UpdateFilterIndices();
    }

    [RelayCommand]
    public void RemoveFilter(TableFilterViewModel filter)
    {
        Filters.Remove(filter);
        UpdateFilterIndices();
        if (Filters.Count == 0 && AvailableColumnNames.Count > 0)
        {
            AddFilter();
        }
    }

    [RelayCommand]
    public async Task ApplyFilterAsync()
    {
        PageNumber = 1;
        await LoadDataAsync();
    }

    [RelayCommand]
    public async Task ClearFilterAsync()
    {
        Filters.Clear();
        RawFilterText = string.Empty;
        HasActiveFilter = false;
        ActiveFilterSummary = string.Empty;
        PageNumber = 1;

        if (AvailableColumnNames.Count > 0)
        {
            AddFilter();
        }

        await LoadDataAsync();
    }

    public async Task QuickFilterAsync(string field, object? value, bool negate = false)
    {
        IsRawFilterMode = false;
        IsFilterBarOpen = true;
        Filters.Clear();

        var filterVm = new TableFilterViewModel
        {
            Field = field,
            Operator = (value == null || value == DBNull.Value)
                ? (negate ? FilterOperator.IsNotNull : FilterOperator.IsNull)
                : (negate ? FilterOperator.NotEquals : FilterOperator.Equals),
            Value = value?.ToString() ?? string.Empty,
            IsFirstFilter = true,
            LogicalOp = "AND",
            AvailableColumns = AvailableColumnNames
        };

        Filters.Add(filterVm);
        PageNumber = 1;
        await LoadDataAsync();
    }

    private void UpdateFilterIndices()
    {
        for (int i = 0; i < Filters.Count; i++)
        {
            Filters[i].IsFirstFilter = (i == 0);
        }
    }

    private void UpdateStagedChangesSummary()
    {
        var inserts = Rows.Count(r => r.IsNewRow && !r.IsMarkedForDeletion);
        var deletes = Rows.Count(r => r.IsMarkedForDeletion);
        var updates = Rows.Count(r => !r.IsNewRow && !r.IsMarkedForDeletion && r.ModifiedColumns.Count > 0);

        var total = inserts + deletes + updates;
        HasStagedChanges = total > 0;

        if (total == 0)
        {
            StagedChangesSummary = string.Empty;
        }
        else
        {
            var parts = new List<string>();
            if (inserts > 0) parts.Add($"{inserts} to insert");
            if (updates > 0) parts.Add($"{updates} to update");
            if (deletes > 0) parts.Add($"{deletes} to delete");
            StagedChangesSummary = string.Join(", ", parts);
        }
    }

    public async Task ExportDataAsync(ExportFormat format, string outputPath)
    {
        IsLoading = true;
        StatusMessage = $"Exporting table '{Table.Name}' to {format}...";

        try
        {
            List<TableFilter>? filterList = null;
            string? rawWhere = null;

            if (IsRawFilterMode && !string.IsNullOrWhiteSpace(RawFilterText))
            {
                rawWhere = RawFilterText;
            }
            else if (!IsRawFilterMode && Filters.Any(f => !string.IsNullOrWhiteSpace(f.Field) && (!f.IsValueRequired || !string.IsNullOrWhiteSpace(f.Value))))
            {
                filterList = Filters
                    .Where(f => !string.IsNullOrWhiteSpace(f.Field) && (!f.IsValueRequired || !string.IsNullOrWhiteSpace(f.Value)))
                    .Select(f => f.ToDomainFilter())
                    .ToList();
            }

            await _exportImportService.ExportTableAsync(_client, Table.Name, Table.Schema, format, outputPath, filterList, rawWhere);
            StatusMessage = $"✓ Successfully exported '{Table.Name}' to {System.IO.Path.GetFileName(outputPath)} ({format}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✕ Export failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedRowChanged(EditableRowViewModel? value)
    {
        if (IsCellInspectorOpen && value != null && CellInspector != null)
        {
            InspectCell(value, CellInspector.ColumnName);
        }
    }

    public void InspectCell(EditableRowViewModel? row, string? columnName)
    {
        if (row == null) row = SelectedRow;
        if (row == null) return;

        if (string.IsNullOrWhiteSpace(columnName))
        {
            columnName = Columns.FirstOrDefault()?.ColumnName;
        }
        if (string.IsNullOrWhiteSpace(columnName)) return;

        var col = Columns.FirstOrDefault(c => c.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        var dataType = col?.DataType ?? "text";

        CellInspector = new CellInspectorViewModel(
            row,
            columnName,
            dataType,
            _clipboardService,
            onApplied: () =>
            {
                UpdateStagedChangesSummary();
            },
            onClose: () =>
            {
                IsCellInspectorOpen = false;
                CellInspector = null;
            });

        IsCellInspectorOpen = true;
    }

    [RelayCommand]
    public void ToggleCellInspector()
    {
        if (IsCellInspectorOpen)
        {
            CloseCellInspector();
        }
        else
        {
            InspectCell(SelectedRow, null);
        }
    }

    [RelayCommand]
    public void CloseCellInspector()
    {
        IsCellInspectorOpen = false;
        CellInspector = null;
    }
}
