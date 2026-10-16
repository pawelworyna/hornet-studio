using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class TablePropertiesViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    public TableOrView Table { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _ddlScript = string.Empty;

    public ObservableCollection<ExtendedTableColumn> Columns { get; } = new();
    public ObservableCollection<TableIndex> Indexes { get; } = new();
    public ObservableCollection<TableKey> Keys { get; } = new();
    public ObservableCollection<TableTrigger> Triggers { get; } = new();

    public TablePropertiesViewModel(IDatabaseClient client, TableOrView table)
    {
        _client = client;
        Table = table;
        Title = $"{table.Name} (Structure)";
        IconKey = "IconSettings";
    }

    [RelayCommand]
    public async Task LoadPropertiesAsync()
    {
        IsLoading = true;
        try
        {
            var cols = await _client.ListTableColumnsAsync(Table.Name, Table.Schema);
            Columns.Clear();
            foreach (var c in cols)
            {
                Columns.Add(c);
            }

            var idxs = await _client.ListTableIndexesAsync(Table.Name, Table.Schema);
            Indexes.Clear();
            foreach (var i in idxs)
            {
                Indexes.Add(i);
            }

            var keys = await _client.GetTableKeysAsync(Table.Name, Table.Schema);
            Keys.Clear();
            foreach (var k in keys)
            {
                Keys.Add(k);
            }

            var trigs = await _client.ListTableTriggersAsync(Table.Name, Table.Schema);
            Triggers.Clear();
            foreach (var t in trigs)
            {
                Triggers.Add(t);
            }

            DdlScript = await _client.GenerateCreateTableSqlAsync(Table.Name, Table.Schema);
        }
        catch (Exception ex)
        {
            DdlScript = $"-- Error loading table schema: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
