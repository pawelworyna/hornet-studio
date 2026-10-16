using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.Views;

public partial class QueryEditorView : UserControl
{
    private QueryEditorViewModel? _viewModel;

    public QueryEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ResultUpdated -= RebuildColumns;
        }

        _viewModel = DataContext as QueryEditorViewModel;

        if (_viewModel != null)
        {
            _viewModel.ResultUpdated += RebuildColumns;
            RebuildColumns();
        }
    }

    private void RebuildColumns()
    {
        if (_viewModel == null || ResultsGrid == null)
            return;

        ResultsGrid.Columns.Clear();

        foreach (var col in _viewModel.Columns)
        {
            var gridCol = new DataGridTextColumn
            {
                Header = col.Name,
                Binding = new Binding($"[{col.Name}]"),
                IsReadOnly = true,
                CanUserSort = true
            };
            ResultsGrid.Columns.Add(gridCol);
        }

        ResultsGrid.ItemsSource = _viewModel.Rows;
    }

    private async void OnExportCsvClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Query Result to CSV",
            DefaultExtension = "csv",
            SuggestedFileName = $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        });

        if (file != null)
        {
            var localPath = file.Path.LocalPath;
            await _viewModel.ExportToCsvAsync(localPath);
        }
    }

    private async void OnExportJsonClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Query Result to JSON",
            DefaultExtension = "json",
            SuggestedFileName = $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        });

        if (file != null)
        {
            var localPath = file.Path.LocalPath;
            await _viewModel.ExportToJsonAsync(localPath);
        }
    }

    private async void OnExportSqlClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Query Result to SQL Inserts",
            DefaultExtension = "sql",
            SuggestedFileName = $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        });

        if (file != null)
        {
            var localPath = file.Path.LocalPath;
            await _viewModel.ExportToSqlAsync(localPath);
        }
    }

    private async void OnExportExcelClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Query Result to Excel Workbook",
            DefaultExtension = "xlsx",
            SuggestedFileName = $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        });

        if (file != null)
        {
            var localPath = file.Path.LocalPath;
            await _viewModel.ExportToExcelAsync(localPath);
        }
    }
}
