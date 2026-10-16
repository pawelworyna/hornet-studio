using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly HornetDbContext _dbContext;
    private readonly IExportImportService _exportImportService;
    private readonly IBackupRestoreService _backupRestoreService;
    private readonly Action _onDisconnect;
    private readonly IClipboardService? _clipboardService;
    private readonly ISqlFormatterService _sqlFormatter;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private bool _isRestoringTabs;

    public ConnectionConfig Config => _client.Config;
    public IDatabaseClient Client => _client;

    [ObservableProperty]
    private SidebarViewModel _sidebar;

    [ObservableProperty]
    private CoreTabsViewModel _tabs;

    [ObservableProperty]
    private string _serverVersion = string.Empty;

    [ObservableProperty]
    private string _connectionSummary = string.Empty;

    [ObservableProperty]
    private bool _isQuickSearchOpen;

    [ObservableProperty]
    private QuickSearchViewModel? _quickSearch;

    public WorkspaceViewModel(
        IDatabaseClient client,
        HornetDbContext dbContext,
        IExportImportService exportImportService,
        IBackupRestoreService backupRestoreService,
        Action onDisconnect,
        IClipboardService? clipboardService = null,
        ISqlFormatterService? sqlFormatter = null)
    {
        _client = client;
        _dbContext = dbContext;
        _exportImportService = exportImportService;
        _backupRestoreService = backupRestoreService;
        _onDisconnect = onDisconnect;
        _clipboardService = clipboardService;
        _sqlFormatter = sqlFormatter ?? new SqlFormatterService();

        Tabs = new CoreTabsViewModel();
        Tabs.TabsChanged += OnTabsChanged;

        Sidebar = new SidebarViewModel(
            client,
            dbContext,
            openTableData: OpenTableData,
            openTableProperties: OpenTableProperties,
            openQuery: OpenQuery,
            clipboardService: clipboardService,
            openTableBuilder: OpenTableBuilderTab);

        ConnectionSummary = $"{Config.Name} • {Config.ConnectionType} ({Config.Host}:{Config.Port}/{Config.DefaultDatabase})";
    }

    public async Task InitializeAsync()
    {
        try
        {
            ServerVersion = await _client.GetVersionAsync();
        }
        catch
        {
            ServerVersion = "Connected";
        }

        await Sidebar.RefreshAsync();

        try
        {
            var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == "session:last_active_connection_id");
            if (setting == null)
            {
                _dbContext.UserSettings.Add(new UserSettingEntity
                {
                    Key = "session:last_active_connection_id",
                    Value = Config.Id.ToString(),
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Value = Config.Id.ToString();
                setting.UpdatedAt = DateTime.UtcNow;
            }
            await _dbContext.SaveChangesAsync();
        }
        catch { }

        await RestoreOpenTabsAsync();
    }

    private CancellationTokenSource? _draftSaveCts;

    private void OnQueryDraftChanged(QueryEditorViewModel sender, string newText)
    {
        if (_isRestoringTabs) return;
        _draftSaveCts?.Cancel();
        _draftSaveCts = new CancellationTokenSource();
        var token = _draftSaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100, token);
                if (!token.IsCancellationRequested)
                {
                    await SaveOpenTabsAsync();
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void OnTabsChanged()
    {
        if (_isRestoringTabs) return;
        foreach (var tab in Tabs.Tabs)
        {
            if (tab is QueryEditorViewModel qvm)
            {
                qvm.QueryDraftChanged -= OnQueryDraftChanged;
                qvm.QueryDraftChanged += OnQueryDraftChanged;
            }
        }
        _ = SaveOpenTabsAsync();
    }

    [RelayCommand]
    public void OpenNewQueryTab()
    {
        var tabIndex = Tabs.Tabs.Count + 1;
        var queryTab = new QueryEditorViewModel(
            _client,
            _dbContext,
            _exportImportService,
            initialQuery: null,
            title: $"Query {tabIndex}",
            sqlFormatter: _sqlFormatter);

        queryTab.QueryDraftChanged += OnQueryDraftChanged;
        Tabs.OpenTab(queryTab);
    }

    [RelayCommand]
    public void OpenTableBuilderTab()
    {
        var builderTab = new TableBuilderViewModel(
            _client,
            Sidebar.SelectedSchema,
            onTableCreated: async () =>
            {
                await Sidebar.RefreshAsync();
            });

        Tabs.OpenTab(builderTab);
    }

    [RelayCommand]
    public void OpenBackupRestoreTab()
    {
        var backupTab = new BackupRestoreViewModel(_client, _backupRestoreService);
        Tabs.OpenTab(backupTab);
    }

    [RelayCommand]
    public void OpenDataImportTab()
    {
        var importTab = new DataImportViewModel(_client, _exportImportService);
        Tabs.OpenTab(importTab);
    }

    [RelayCommand]
    public void OpenQuickSearch()
    {
        var tables = Sidebar.AllTablesAndViews;
        var favorites = Sidebar.SavedQueries;
        var history = Sidebar.QueryHistory;

        QuickSearch = new QuickSearchViewModel(
            tables,
            favorites,
            history,
            openTableData: OpenTableData,
            openTableProperties: OpenTableProperties,
            openQuery: OpenQuery,
            closeModal: CloseQuickSearch);

        IsQuickSearchOpen = true;
    }

    [RelayCommand]
    public void CloseQuickSearch()
    {
        IsQuickSearchOpen = false;
        QuickSearch = null;
    }

    public void OpenTableData(TableOrView table)
    {
        var tableTab = new TableDataViewModel(_client, _exportImportService, table, _clipboardService);
        Tabs.OpenTab(tableTab);
        _ = tableTab.LoadDataAsync();
    }

    public void OpenTableProperties(TableOrView table)
    {
        var propTab = new TablePropertiesViewModel(_client, table);
        Tabs.OpenTab(propTab);
        _ = propTab.LoadPropertiesAsync();
    }

    public void OpenQuery(string title, string sql)
    {
        var queryTab = new QueryEditorViewModel(
            _client,
            _dbContext,
            _exportImportService,
            initialQuery: sql,
            title: title,
            sqlFormatter: _sqlFormatter);

        queryTab.QueryDraftChanged += OnQueryDraftChanged;
        Tabs.OpenTab(queryTab);
    }

    public async Task RestoreOpenTabsAsync()
    {
        _isRestoringTabs = true;
        try
        {
            try
            {
                var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == "session:last_active_connection_id");
                if (setting == null)
                {
                    _dbContext.UserSettings.Add(new UserSettingEntity
                    {
                        Key = "session:last_active_connection_id",
                        Value = Config.Id.ToString(),
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    setting.Value = Config.Id.ToString();
                    setting.UpdatedAt = DateTime.UtcNow;
                }
                await _dbContext.SaveChangesAsync();
            }
            catch { }

            var savedTabs = await _dbContext.OpenTabs
                .Where(t => t.ConnectionId == Config.Id)
                .OrderBy(t => t.Position)
                .ToListAsync();

            if (savedTabs.Count == 0)
            {
                OpenNewQueryTab();
                return;
            }

            TabItemViewModelBase? activeTab = null;

            foreach (var tabEntity in savedTabs)
            {
                TabItemViewModelBase? tab = null;
                switch (tabEntity.TabType)
                {
                    case "query":
                        var qvm = new QueryEditorViewModel(
                            _client,
                            _dbContext,
                            _exportImportService,
                            initialQuery: tabEntity.QueryText,
                            title: string.IsNullOrWhiteSpace(tabEntity.Title) ? "SQL Query" : tabEntity.Title,
                            sqlFormatter: _sqlFormatter);
                        qvm.QueryDraftChanged += OnQueryDraftChanged;
                        tab = qvm;
                        break;

                    case "table":
                        if (!string.IsNullOrEmpty(tabEntity.TableName))
                        {
                            var table = new TableOrView(tabEntity.TableName, tabEntity.SchemaName ?? Sidebar.SelectedSchema);
                            var tableTab = new TableDataViewModel(_client, _exportImportService, table, _clipboardService);
                            tab = tableTab;
                            _ = tableTab.LoadDataAsync();
                        }
                        break;

                    case "table-properties":
                        if (!string.IsNullOrEmpty(tabEntity.TableName))
                        {
                            var table = new TableOrView(tabEntity.TableName, tabEntity.SchemaName ?? Sidebar.SelectedSchema);
                            var propTab = new TablePropertiesViewModel(_client, table);
                            tab = propTab;
                            _ = propTab.LoadPropertiesAsync();
                        }
                        break;

                    case "table-builder":
                        tab = new TableBuilderViewModel(
                            _client,
                            Sidebar.SelectedSchema,
                            onTableCreated: async () => await Sidebar.RefreshAsync());
                        break;

                    case "backup":
                        tab = new BackupRestoreViewModel(_client, _backupRestoreService);
                        break;

                    case "import":
                        tab = new DataImportViewModel(_client, _exportImportService);
                        break;
                }

                if (tab != null)
                {
                    Tabs.OpenTab(tab);
                    if (tabEntity.IsActive)
                    {
                        activeTab = tab;
                    }
                }
            }

            if (activeTab != null)
            {
                Tabs.SelectTab(activeTab);
            }
            else if (Tabs.Tabs.Count == 0)
            {
                OpenNewQueryTab();
            }
        }
        catch
        {
            if (Tabs.Tabs.Count == 0)
            {
                OpenNewQueryTab();
            }
        }
        finally
        {
            _isRestoringTabs = false;
        }
    }

    public async Task SaveOpenTabsAsync()
    {
        if (_isRestoringTabs) return;

        await _saveSemaphore.WaitAsync();
        try
        {
            var existing = await _dbContext.OpenTabs
                .Where(t => t.ConnectionId == Config.Id)
                .ToListAsync();

            if (existing.Count > 0)
            {
                _dbContext.OpenTabs.RemoveRange(existing);
            }

            var snapshot = Tabs.Tabs.ToList();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var tab = snapshot[i];
                var entity = new OpenTabEntity
                {
                    ConnectionId = Config.Id,
                    Position = i,
                    IsActive = (tab == Tabs.SelectedTab)
                };

                if (tab is QueryEditorViewModel qvm)
                {
                    entity.TabType = "query";
                    entity.Title = qvm.Title;
                    entity.QueryText = qvm.QueryText;
                }
                else if (tab is TableDataViewModel td)
                {
                    entity.TabType = "table";
                    entity.Title = td.Title;
                    entity.TableName = td.Table.Name;
                    entity.SchemaName = td.Table.Schema;
                }
                else if (tab is TablePropertiesViewModel tp)
                {
                    entity.TabType = "table-properties";
                    entity.Title = tp.Title;
                    entity.TableName = tp.Table.Name;
                    entity.SchemaName = tp.Table.Schema;
                }
                else if (tab is TableBuilderViewModel tb)
                {
                    entity.TabType = "table-builder";
                    entity.Title = tb.Title;
                }
                else if (tab is BackupRestoreViewModel br)
                {
                    entity.TabType = "backup";
                    entity.Title = br.Title;
                }
                else if (tab is DataImportViewModel di)
                {
                    entity.TabType = "import";
                    entity.Title = di.Title;
                }
                else
                {
                    continue;
                }

                _dbContext.OpenTabs.Add(entity);
            }

            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Ignore persistence errors gracefully
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        try
        {
            await SaveOpenTabsAsync();
        }
        catch
        {
        }

        try
        {
            await _client.DisconnectAsync();
            await _client.DisposeAsync();
        }
        catch
        {
            // Ignore error on disconnect
        }

        _onDisconnect();
    }
}
