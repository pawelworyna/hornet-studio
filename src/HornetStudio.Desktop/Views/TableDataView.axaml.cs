using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using HornetStudio.Desktop.ViewModels;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.Views;

public partial class TableDataView : UserControl
{
    private TableDataViewModel? _viewModel;

    public TableDataView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.DataRefreshed -= RebuildColumns;
        }

        _viewModel = DataContext as TableDataViewModel;

        if (_viewModel != null)
        {
            _viewModel.DataRefreshed += RebuildColumns;
            RebuildColumns();
        }
    }

    private void RebuildColumns()
    {
        if (_viewModel == null || TableGrid == null)
            return;

        TableGrid.Columns.Clear();

        foreach (var col in _viewModel.Columns)
        {
            var gridCol = new DataGridTextColumn
            {
                Header = col.ColumnName + (col.IsPrimaryKey ? " 🔑" : ""),
                Binding = new Binding($"[{col.ColumnName}]") { Mode = BindingMode.TwoWay },
                IsReadOnly = false,
                CanUserSort = true
            };
            TableGrid.Columns.Add(gridCol);
        }

        TableGrid.ItemsSource = _viewModel.Rows;
    }

    private void OnInspectCellClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || TableGrid == null) return;
        if (TableGrid.SelectedItem is not EditableRowViewModel row) return;

        var col = TableGrid.CurrentColumn as DataGridTextColumn;
        var colName = col?.Header?.ToString()?.Replace(" 🔑", "").Trim();
        _viewModel.InspectCell(row, colName);
    }

    private async void OnFilterByCellClicked(object? sender, RoutedEventArgs e)
    {
        await ApplyQuickFilterAsync(negate: false);
    }

    private async void OnFilterExcludeCellClicked(object? sender, RoutedEventArgs e)
    {
        await ApplyQuickFilterAsync(negate: true);
    }

    private async System.Threading.Tasks.Task ApplyQuickFilterAsync(bool negate)
    {
        if (_viewModel == null || TableGrid == null) return;
        if (TableGrid.SelectedItem is not EditableRowViewModel row) return;

        var col = TableGrid.CurrentColumn as DataGridTextColumn;
        var colName = col?.Header?.ToString()?.Replace(" 🔑", "").Trim();
        if (string.IsNullOrWhiteSpace(colName))
        {
            colName = _viewModel.Columns.FirstOrDefault()?.ColumnName;
        }

        if (string.IsNullOrWhiteSpace(colName)) return;

        var value = row[colName];
        await _viewModel.QuickFilterAsync(colName, value, negate);
    }

    private async void OnExportCsvClicked(object? sender, RoutedEventArgs e)
    {
        await ExportDataWithPickerAsync(HornetStudio.Core.Services.ExportFormat.Csv, "csv", "CSV Files (*.csv)");
    }

    private async void OnExportJsonClicked(object? sender, RoutedEventArgs e)
    {
        await ExportDataWithPickerAsync(HornetStudio.Core.Services.ExportFormat.Json, "json", "JSON Files (*.json)");
    }

    private async void OnExportSqlClicked(object? sender, RoutedEventArgs e)
    {
        await ExportDataWithPickerAsync(HornetStudio.Core.Services.ExportFormat.Sql, "sql", "SQL Files (*.sql)");
    }

    private async void OnExportExcelClicked(object? sender, RoutedEventArgs e)
    {
        await ExportDataWithPickerAsync(HornetStudio.Core.Services.ExportFormat.Xlsx, "xlsx", "Excel Workbook (*.xlsx)");
    }

    private async System.Threading.Tasks.Task ExportDataWithPickerAsync(
        HornetStudio.Core.Services.ExportFormat format,
        string extension,
        string filterName)
    {
        if (_viewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var tableName = _viewModel.Table?.Name ?? "table";
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = $"Export {tableName} to {format}",
            DefaultExtension = extension,
            SuggestedFileName = $"{tableName}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType(filterName) { Patterns = new[] { $"*.{extension}" } }
            }
        });

        if (file != null)
        {
            var localPath = file.Path.LocalPath;
            await _viewModel.ExportDataAsync(format, localPath);
        }
    }
}
