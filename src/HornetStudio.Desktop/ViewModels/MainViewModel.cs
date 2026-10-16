using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HornetStudio.Core.Common;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Security;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly HornetDbContext _dbContext;
    private readonly MasterPasswordVault _vault;
    private readonly IDatabaseClientFactory _clientFactory;
    private readonly IExportImportService _exportImportService;
    private readonly IBackupRestoreService _backupRestoreService;
    private readonly IClipboardService? _clipboardService;
    private readonly ISqlFormatterService? _sqlFormatter;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _appTitle = $"HornetStudio {AppVersion.DisplayVersion}";

    public string VersionDisplay => $"HornetStudio {AppVersion.DisplayVersion}";

    public MainViewModel(
        HornetDbContext dbContext,
        MasterPasswordVault vault,
        IDatabaseClientFactory clientFactory,
        IExportImportService exportImportService,
        IBackupRestoreService backupRestoreService,
        IClipboardService? clipboardService = null,
        ISqlFormatterService? sqlFormatter = null)
    {
        _dbContext = dbContext;
        _vault = vault;
        _clientFactory = clientFactory;
        _exportImportService = exportImportService;
        _backupRestoreService = backupRestoreService;
        _clipboardService = clipboardService;
        _sqlFormatter = sqlFormatter;

        ShowConnectionManager();
    }

    // Default parameterless constructor for XAML previewer
    public MainViewModel()
    {
        _dbContext = null!;
        _vault = null!;
        _clientFactory = null!;
        _exportImportService = null!;
        _backupRestoreService = null!;
        _clipboardService = null;
    }

    public void ShowConnectionManager()
    {
        var connManager = new ConnectionManagerViewModel(
            _dbContext,
            _vault,
            _clientFactory,
            onConnected: OnConnectedToDatabase);

        CurrentPage = connManager;
        AppTitle = $"HornetStudio {AppVersion.DisplayVersion} - Connection Manager";
        StatusText = "Ready";

        _ = connManager.InitializeAsync();
    }

    private void OnConnectedToDatabase(IDatabaseClient client)
    {
        var workspace = new WorkspaceViewModel(
            client,
            _dbContext,
            _exportImportService,
            _backupRestoreService,
            onDisconnect: ShowConnectionManager,
            clipboardService: _clipboardService,
            sqlFormatter: _sqlFormatter);

        CurrentPage = workspace;
        AppTitle = $"HornetStudio {AppVersion.DisplayVersion} - {client.Config.Name} ({client.Config.ConnectionType})";
        StatusText = $"Connected to {client.Config.Name}";

        _ = workspace.InitializeAsync();
    }
}
