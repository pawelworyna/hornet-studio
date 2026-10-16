using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.Views;

public partial class DataImportView : UserControl
{
    private DataImportViewModel? _viewModel;

    public DataImportView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as DataImportViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(DataImportViewModel.PreviewColumns) ||
                    args.PropertyName == nameof(DataImportViewModel.PreviewRows))
                {
                    RebuildPreviewGrid();
                }
            };
        }
    }

    private void RebuildPreviewGrid()
    {
        if (_viewModel == null || PreviewGrid == null) return;

        PreviewGrid.Columns.Clear();
        foreach (var header in _viewModel.PreviewColumns)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{header}]"),
                IsReadOnly = true
            };
            PreviewGrid.Columns.Add(col);
        }

        PreviewGrid.ItemsSource = _viewModel.PreviewRows;
    }

    private async void OnBrowseFileClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Data File to Import (CSV or JSON)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported Data Files (*.csv, *.json)") { Patterns = new[] { "*.csv", "*.json", "*.txt" } },
                new FilePickerFileType("CSV Files (*.csv)") { Patterns = new[] { "*.csv", "*.txt" } },
                new FilePickerFileType("JSON Files (*.json)") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            _viewModel.SourceFilePath = files[0].Path.LocalPath;
            await _viewModel.LoadPreviewAsync();
            RebuildPreviewGrid();
        }
    }

    private void OnBrowseCsvFileClicked(object? sender, RoutedEventArgs e) => OnBrowseFileClicked(sender, e);
}
