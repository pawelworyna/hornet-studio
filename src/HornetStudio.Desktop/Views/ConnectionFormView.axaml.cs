using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Desktop.Views;

public partial class ConnectionFormView : UserControl
{
    private ConnectionFormViewModel? ViewModel => DataContext as ConnectionFormViewModel;

    public ConnectionFormView()
    {
        InitializeComponent();
    }

    private async void OnBrowseSqliteFileClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var typeSpecificFilter = ViewModel.ConnectionType switch
        {
            HornetStudio.Core.Domain.Enums.ConnectionType.MsAccess =>
                new FilePickerFileType("Microsoft Access Databases (*.accdb, *.mdb)") { Patterns = new[] { "*.accdb", "*.mdb" } },
            HornetStudio.Core.Domain.Enums.ConnectionType.DuckDb =>
                new FilePickerFileType("DuckDB Databases (*.duckdb, *.db)") { Patterns = new[] { "*.duckdb", "*.db" } },
            _ =>
                new FilePickerFileType("SQLite Databases (*.db, *.sqlite, *.sqlite3, *.db3)") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3", "*.db3", "*.s3db", "*.sl3" } }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {ViewModel.ConnectionType} Database File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                typeSpecificFilter,
                new FilePickerFileType("All Files (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            ViewModel.SetFileDatabasePath(files[0].Path.LocalPath);
        }
    }

    private async void OnCreateSqliteFileClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New SQLite Database File",
            DefaultExtension = "db",
            SuggestedFileName = "new_database.db",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SQLite Database (*.db)") { Patterns = new[] { "*.db" } }
            }
        });

        if (file != null)
        {
            ViewModel.SetSqliteDatabasePath(file.Path.LocalPath);
        }
    }

    private void OnInMemorySqliteClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SetSqliteDatabasePath(":memory:");
    }
}
