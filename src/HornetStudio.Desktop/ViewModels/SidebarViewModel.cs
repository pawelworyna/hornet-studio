using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly IDatabaseClient _client;
    private readonly HornetDbContext _dbContext;
    private readonly Action<TableOrView> _openTableData;
    private readonly Action<TableOrView> _openTableProperties;
    private readonly Action<string, string> _openQuery;
    private readonly Action? _openTableBuilder;
    private readonly IClipboardService _clipboardService;
    private readonly HashSet<string> _pinnedKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TableOrView? _selectedTableOrView;

    [ObservableProperty]
    private FavoriteQueryEntity? _selectedFavoriteQuery;

    [ObservableProperty]
    private string? _selectedSchema;

    [ObservableProperty]
    private string _schemaLabel = "Schema";

    [ObservableProperty]
    private bool _canSwitchSchema;

    [ObservableProperty]
    private bool _hasPinnedItems;

    public ObservableCollection<string> AvailableSchemas { get; } = new();
    public ObservableCollection<TableOrView> AllTablesAndViews { get; } = new();
    public ObservableCollection<TableOrView> PinnedItems { get; } = new();
    public ObservableCollection<TableOrView> FilteredPinnedItems { get; } = new();
    public ObservableCollection<TableOrView> FilteredTables { get; } = new();
    public ObservableCollection<TableOrView> FilteredViews { get; } = new();
    public ObservableCollection<FavoriteQueryEntity> SavedQueries { get; } = new();
    public ObservableCollection<QueryHistoryEntity> QueryHistory { get; } = new();

    public SidebarViewModel(
        IDatabaseClient client,
        HornetDbContext dbContext,
        Action<TableOrView> openTableData,
        Action<TableOrView> openTableProperties,
        Action<string, string> openQuery,
        IClipboardService? clipboardService = null,
        Action? openTableBuilder = null)
    {
        _client = client;
        _dbContext = dbContext;
        _openTableData = openTableData;
        _openTableProperties = openTableProperties;
        _openQuery = openQuery;
        _clipboardService = clipboardService ?? new NullClipboardService();
        _openTableBuilder = openTableBuilder;
    }

    [RelayCommand]
    public void OpenTableBuilder()
    {
        _openTableBuilder?.Invoke();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSchemaChanged(string? value)
    {
        if (!IsLoading)
        {
            _ = LoadTablesAndViewsAsync(value);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var features = await _client.GetSupportedFeaturesAsync();
            IReadOnlyList<string> schemaList = [];

            if (features.Schemas)
            {
                SchemaLabel = "Schema";
                schemaList = await _client.ListSchemasAsync();
            }
            else if (_client.ConnectionType != ConnectionType.Sqlite)
            {
                SchemaLabel = "Database";
                schemaList = await _client.ListDatabasesAsync();
            }
            else
            {
                SchemaLabel = "Database";
                schemaList = ["main"];
            }

            AvailableSchemas.Clear();
            foreach (var s in schemaList)
            {
                AvailableSchemas.Add(s);
            }

            CanSwitchSchema = AvailableSchemas.Count > 1;

            if (string.IsNullOrEmpty(SelectedSchema) || !AvailableSchemas.Contains(SelectedSchema))
            {
                if (AvailableSchemas.Contains("public"))
                {
                    SelectedSchema = "public";
                }
                else if (AvailableSchemas.Contains("dbo"))
                {
                    SelectedSchema = "dbo";
                }
                else if (!string.IsNullOrEmpty(_client.Config.DefaultDatabase) && AvailableSchemas.Contains(_client.Config.DefaultDatabase))
                {
                    SelectedSchema = _client.Config.DefaultDatabase;
                }
                else
                {
                    SelectedSchema = AvailableSchemas.FirstOrDefault();
                }
            }

            await LoadTablesAndViewsInternalAsync(SelectedSchema);
            await LoadPinnedAsync();

            // Load saved queries
            var favorites = await _dbContext.FavoriteQueries
                .OrderByDescending(f => f.CreatedAt)
                .Take(50)
                .ToListAsync();

            SavedQueries.Clear();
            foreach (var f in favorites)
            {
                SavedQueries.Add(f);
            }

            // Load query history
            var history = await _dbContext.QueryHistory
                .OrderByDescending(h => h.ExecutedAt)
                .Take(50)
                .ToListAsync();

            QueryHistory.Clear();
            foreach (var h in history)
            {
                QueryHistory.Add(h);
            }
        }
        catch
        {
            // Ignore error
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadTablesAndViewsAsync(string? schema)
    {
        IsLoading = true;
        try
        {
            await LoadTablesAndViewsInternalAsync(schema);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTablesAndViewsInternalAsync(string? schema)
    {
        var tables = await _client.ListTablesAsync(schema);
        var views = await _client.ListViewsAsync(schema);

        AllTablesAndViews.Clear();
        foreach (var t in tables)
        {
            AllTablesAndViews.Add(t);
        }
        foreach (var v in views)
        {
            AllTablesAndViews.Add(v);
        }

        SyncPinnedItemsWithAvailable();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredPinnedItems.Clear();
        FilteredTables.Clear();
        FilteredViews.Clear();

        var query = FilterText?.Trim() ?? string.Empty;

        foreach (var item in PinnedItems)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.Schema != null && item.Schema.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredPinnedItems.Add(item);
            }
        }

        HasPinnedItems = FilteredPinnedItems.Count > 0;

        foreach (var item in AllTablesAndViews)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.Schema != null && item.Schema.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                if (item.IsView)
                {
                    FilteredViews.Add(item);
                }
                else
                {
                    FilteredTables.Add(item);
                }
            }
        }
    }

    private string GetPinnedSettingKey() => $"pinned_tables_{_client.Config.Id}";

    public string GetItemKey(TableOrView item) =>
        string.IsNullOrWhiteSpace(item.Schema) ? item.Name : $"{item.Schema}.{item.Name}";

    public bool IsPinned(TableOrView item) => _pinnedKeys.Contains(GetItemKey(item));

    [RelayCommand]
    public async Task TogglePinAsync(TableOrView? item)
    {
        if (item == null) return;

        var key = GetItemKey(item);
        if (_pinnedKeys.Contains(key))
        {
            _pinnedKeys.Remove(key);
            var existing = PinnedItems.FirstOrDefault(p => string.Equals(GetItemKey(p), key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                PinnedItems.Remove(existing);
            }
        }
        else
        {
            _pinnedKeys.Add(key);
            if (!PinnedItems.Any(p => string.Equals(GetItemKey(p), key, StringComparison.OrdinalIgnoreCase)))
            {
                PinnedItems.Add(item);
            }
        }

        ApplyFilter();
        await SavePinnedAsync();
    }

    public async Task LoadPinnedAsync()
    {
        try
        {
            var key = GetPinnedSettingKey();
            var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == key);
            _pinnedKeys.Clear();
            PinnedItems.Clear();

            if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
            {
                var keys = JsonSerializer.Deserialize<List<string>>(setting.Value);
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        _pinnedKeys.Add(k);
                    }
                }
            }

            SyncPinnedItemsWithAvailable();
        }
        catch
        {
            // Ignore load error
        }
    }

    private void SyncPinnedItemsWithAvailable()
    {
        PinnedItems.Clear();
        foreach (var key in _pinnedKeys)
        {
            var match = AllTablesAndViews.FirstOrDefault(t => string.Equals(GetItemKey(t), key, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                PinnedItems.Add(match);
            }
            else
            {
                var parts = key.Split('.', 2);
                if (parts.Length == 2)
                {
                    PinnedItems.Add(new TableOrView(parts[1], parts[0]));
                }
                else
                {
                    PinnedItems.Add(new TableOrView(parts[0]));
                }
            }
        }
        ApplyFilter();
    }

    private async Task SavePinnedAsync()
    {
        try
        {
            var key = GetPinnedSettingKey();
            var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == key);
            var json = JsonSerializer.Serialize(_pinnedKeys.ToList());

            if (setting == null)
            {
                _dbContext.UserSettings.Add(new UserSettingEntity
                {
                    Key = key,
                    Value = json,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Value = json;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Ignore persistence error
        }
    }

    [RelayCommand]
    public void OpenTableData(TableOrView item)
    {
        _openTableData(item);
    }

    [RelayCommand]
    public void OpenTableProperties(TableOrView item)
    {
        _openTableProperties(item);
    }

    [RelayCommand]
    public void OpenSavedQuery(FavoriteQueryEntity query)
    {
        _openQuery(query.Title, query.QueryText);
    }

    [RelayCommand]
    public void OpenHistoryItem(QueryHistoryEntity history)
    {
        _openQuery($"History: {history.ExecutedAt:HH:mm:ss}", history.QueryText);
    }

    [RelayCommand]
    public async Task CopyTableNameAsync(TableOrView? item)
    {
        if (item == null) return;
        await _clipboardService.SetTextAsync(item.Name);
    }

    [RelayCommand]
    public async Task CopyQualifiedNameAsync(TableOrView? item)
    {
        if (item == null) return;
        var qualified = GetQualifiedName(item);
        await _clipboardService.SetTextAsync(qualified);
    }

    [RelayCommand]
    public void GenerateSelectQuery(TableOrView? item)
    {
        if (item == null) return;
        var qualified = GetQualifiedName(item);
        string sql;

        if (_client.ConnectionType == ConnectionType.SqlServer)
        {
            sql = $"SELECT TOP 100 * FROM {qualified};";
        }
        else if (_client.ConnectionType == ConnectionType.MongoDb)
        {
            sql = $"db.{item.Name}.find().limit(100)";
        }
        else
        {
            sql = $"SELECT * FROM {qualified} LIMIT 100;";
        }

        _openQuery($"SELECT: {item.Name}", sql);
    }

    [RelayCommand]
    public async Task GenerateInsertQueryAsync(TableOrView? item)
    {
        if (item == null) return;

        if (_client.ConnectionType == ConnectionType.MongoDb)
        {
            var mql = $"db.{item.Name}.insertOne({{\n  \"field\": \"value\"\n}})";
            _openQuery($"INSERT: {item.Name}", mql);
            return;
        }

        var qualified = GetQualifiedName(item);
        try
        {
            var cols = await _client.ListTableColumnsAsync(item.Name, item.Schema);
            var insertCols = cols.Where(c => !c.IsAutoIncrement && !c.IsGenerated).ToList();
            if (insertCols.Count == 0) insertCols = cols.ToList();

            if (insertCols.Count > 0)
            {
                var colNames = string.Join(", ", insertCols.Select(c => _client.WrapIdentifier(c.ColumnName)));
                var colPlaceholders = string.Join(", ", insertCols.Select(c => $"/* {c.DataType} */ NULL"));
                var insertSql = $"INSERT INTO {qualified} ({colNames})\nVALUES ({colPlaceholders});";
                _openQuery($"INSERT: {item.Name}", insertSql);
                return;
            }
        }
        catch
        {
            // Fallback if column metadata is not accessible
        }

        var fallbackSql = $"INSERT INTO {qualified} (/* column1, column2 */)\nVALUES (/* val1, val2 */);";
        _openQuery($"INSERT: {item.Name}", fallbackSql);
    }

    [RelayCommand]
    public async Task GenerateCreateScriptAsync(TableOrView? item)
    {
        if (item == null) return;
        try
        {
            var ddl = await _client.GenerateCreateTableSqlAsync(item.Name, item.Schema);
            _openQuery($"DDL: {item.Name}", ddl);
        }
        catch (Exception ex)
        {
            _openQuery($"DDL: {item.Name}", $"-- Error retrieving DDL for {item.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    public void GenerateTruncateQuery(TableOrView? item)
    {
        if (item == null) return;
        var qualified = GetQualifiedName(item);
        string sql;

        if (_client.ConnectionType == ConnectionType.Sqlite)
        {
            sql = $"-- WARNING: Truncates all rows in table!\nDELETE FROM {qualified};";
        }
        else if (_client.ConnectionType == ConnectionType.MongoDb)
        {
            sql = $"-- WARNING: Deletes all documents in collection!\ndb.{item.Name}.deleteMany({{}})";
        }
        else
        {
            sql = $"-- WARNING: Truncates all rows in table!\nTRUNCATE TABLE {qualified};";
        }

        _openQuery($"TRUNCATE: {item.Name}", sql);
    }

    [RelayCommand]
    public void GenerateDropQuery(TableOrView? item)
    {
        if (item == null) return;
        var qualified = GetQualifiedName(item);
        var targetType = item.IsView ? "VIEW" : "TABLE";

        string sql;
        if (_client.ConnectionType == ConnectionType.MongoDb)
        {
            sql = $"-- CAUTION: Drops entire collection permanently!\ndb.{item.Name}.drop()";
        }
        else
        {
            sql = $"-- CAUTION: Drops {targetType.ToLowerInvariant()} permanently!\nDROP {targetType} {qualified};";
        }

        _openQuery($"DROP: {item.Name}", sql);
    }

    public string GetQualifiedName(TableOrView item)
    {
        if (string.IsNullOrWhiteSpace(item.Schema))
        {
            return _client.WrapIdentifier(item.Name);
        }
        return $"{_client.WrapIdentifier(item.Schema)}.{_client.WrapIdentifier(item.Name)}";
    }
}

internal sealed class NullClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => Task.CompletedTask;
}
