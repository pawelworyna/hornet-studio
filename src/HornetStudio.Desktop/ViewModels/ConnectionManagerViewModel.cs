using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Core.Storage.Migration;
using HornetStudio.Core.Storage.Security;
using Microsoft.EntityFrameworkCore;

using HornetStudio.Core.Common;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class ConnectionManagerViewModel : ViewModelBase
{
    private readonly HornetDbContext _dbContext;
    private readonly MasterPasswordVault _vault;
    private readonly IDatabaseClientFactory _clientFactory;
    private readonly Action<IDatabaseClient> _onConnected;

    public string AppVersionText => AppVersion.DisplayVersion;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ConnectionItemViewModel? _selectedConnection;

    [ObservableProperty]
    private ConnectionFormViewModel? _currentForm;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isMasterPasswordPromptOpen;

    [ObservableProperty]
    private string _newMasterPassword = string.Empty;

    [ObservableProperty]
    private ConnectionItemViewModel? _lastSessionConnection;

    public ObservableCollection<ConnectionItemViewModel> AllConnections { get; } = new();
    public ObservableCollection<ConnectionItemViewModel> FilteredConnections { get; } = new();

    public bool HasConnections => FilteredConnections.Count > 0;
    public bool HasNoConnections => FilteredConnections.Count == 0;
    public bool HasLastSession => LastSessionConnection != null;
    public bool IsEditing => CurrentForm != null;
    public bool IsNotEditing => CurrentForm == null;

    public ConnectionManagerViewModel(
        HornetDbContext dbContext,
        MasterPasswordVault vault,
        IDatabaseClientFactory clientFactory,
        Action<IDatabaseClient> onConnected)
    {
        _dbContext = dbContext;
        _vault = vault;
        _clientFactory = clientFactory;
        _onConnected = onConnected;
    }

    public async Task InitializeAsync()
    {
        await ReloadConnectionsAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnCurrentFormChanged(ConnectionFormViewModel? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsNotEditing));
    }

    private void ApplyFilter()
    {
        FilteredConnections.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;

        foreach (var conn in AllConnections)
        {
            if (string.IsNullOrEmpty(q) ||
                conn.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                conn.Host.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (conn.Database != null && conn.Database.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredConnections.Add(conn);
            }
        }

        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(HasNoConnections));
    }

    [RelayCommand]
    public async Task ReloadConnectionsAsync()
    {
        // 1. Automatyczne usuwanie duplikatów połączeń o tej samej nazwie i ścieżce/hoście
        var allSaved = await _dbContext.SavedConnections.ToListAsync();
        var duplicates = allSaved
            .GroupBy(c => new { c.Name, c.ConnectionType, c.DatabasePath, c.Host })
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            foreach (var group in duplicates)
            {
                var toRemove = group.Skip(1).ToList();
                _dbContext.SavedConnections.RemoveRange(toRemove);
            }
            await _dbContext.SaveChangesAsync();
        }

        var entities = await _dbContext.SavedConnections
            .OrderBy(c => c.Name)
            .ToListAsync();

        AllConnections.Clear();
        foreach (var e in entities)
        {
            AllConnections.Add(new ConnectionItemViewModel(e));
        }

        try
        {
            var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == "session:last_active_connection_id");
            if (setting != null && Guid.TryParse(setting.Value, out var lastId))
            {
                LastSessionConnection = AllConnections.FirstOrDefault(c => c.Entity.Id == lastId);
                if (SelectedConnection == null && LastSessionConnection != null)
                {
                    SelectedConnection = LastSessionConnection;
                }
            }
            else
            {
                LastSessionConnection = null;
            }
        }
        catch
        {
            LastSessionConnection = null;
        }

        OnPropertyChanged(nameof(HasLastSession));
        ApplyFilter();
    }

    [RelayCommand]
    public async Task RestoreLastSessionAsync()
    {
        if (LastSessionConnection != null)
        {
            await ConnectAsync(LastSessionConnection);
        }
    }

    [RelayCommand]
    public void NewConnection()
    {
        CurrentForm = new ConnectionFormViewModel(
            _dbContext,
            _vault,
            _clientFactory,
            onSavedOrCancelled: async () =>
            {
                CurrentForm = null;
                await ReloadConnectionsAsync();
            });
    }

    [RelayCommand]
    public void EditConnection(ConnectionItemViewModel? item)
    {
        var target = item ?? SelectedConnection;
        if (target == null)
            return;

        CurrentForm = new ConnectionFormViewModel(
            _dbContext,
            _vault,
            _clientFactory,
            onSavedOrCancelled: async () =>
            {
                CurrentForm = null;
                await ReloadConnectionsAsync();
            },
            existing: target.Entity);
    }

    [RelayCommand]
    public async Task DeleteConnectionAsync(ConnectionItemViewModel? item)
    {
        var target = item ?? SelectedConnection;
        if (target == null)
            return;

        _dbContext.SavedConnections.Remove(target.Entity);
        await _dbContext.SaveChangesAsync();
        await ReloadConnectionsAsync();
        StatusMessage = $"Deleted connection '{target.Name}'.";
    }

    [RelayCommand]
    public async Task ConnectAsync(ConnectionItemViewModel? item)
    {
        var target = item ?? SelectedConnection;
        if (target == null || IsConnecting)
            return;

        IsConnecting = true;
        StatusMessage = $"Connecting to {target.Name}...";

        try
        {
            var config = new ConnectionConfig
            {
                Id = target.Entity.Id,
                Name = target.Entity.Name,
                ConnectionType = target.Entity.ConnectionType,
                Host = target.Entity.Host,
                Port = target.Entity.Port,
                DefaultDatabase = target.Entity.DefaultDatabase,
                Username = target.Entity.Username,
                DatabasePath = target.Entity.DatabasePath,
                LabelColor = target.Entity.LabelColor ?? "#10B981"
            };

            if (!string.IsNullOrEmpty(target.Entity.EncryptedPassword))
            {
                try
                {
                    config.Password = _vault.Decrypt(target.Entity.EncryptedPassword);
                }
                catch
                {
                    // Fallback to empty
                }
            }

            if (!string.IsNullOrEmpty(target.Entity.SslConfigJson))
            {
                try
                {
                    config.Ssl = JsonSerializer.Deserialize<SslConfig>(target.Entity.SslConfigJson) ?? new();
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(target.Entity.SshConfigJson))
            {
                try
                {
                    config.Ssh = JsonSerializer.Deserialize<SshTunnelConfig>(target.Entity.SshConfigJson) ?? new();
                }
                catch { }
            }

            var client = _clientFactory.CreateClient(config);
            await client.ConnectAsync();

            StatusMessage = "Connected!";
            _onConnected(client);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✕ Connection Failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    public async Task ImportExternalConnectionsAsync()
    {
        StatusMessage = "Checking for external connections to import...";
        try
        {
            var importer = new ExternalConnectionImporter();
            var result = await importer.ImportAsync(_dbContext, _vault);

            if (result.ImportedConnections > 0 || result.ImportedQueries > 0)
            {
                StatusMessage = $"✓ Successfully imported {result.ImportedConnections} connections and {result.ImportedQueries} saved queries!";
                await ReloadConnectionsAsync();
            }
            else
            {
                StatusMessage = "No existing database profiles found to import.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import notice: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ToggleMasterPasswordDialog()
    {
        IsMasterPasswordPromptOpen = !IsMasterPasswordPromptOpen;
    }

    [RelayCommand]
    public async Task SaveMasterPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMasterPassword))
        {
            StatusMessage = "Master password cannot be empty.";
            return;
        }

        try
        {
            var success = _vault.SetMasterPassword(NewMasterPassword);
            if (success)
            {
                IsMasterPasswordPromptOpen = false;
                NewMasterPassword = string.Empty;
                StatusMessage = "✓ Master password successfully configured!";
            }
            else
            {
                StatusMessage = "✕ Failed to set master password.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to set master password: {ex.Message}";
        }

        await Task.CompletedTask;
    }
}
