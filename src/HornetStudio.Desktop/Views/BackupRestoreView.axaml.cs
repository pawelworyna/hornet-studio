using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HornetStudio.Core.Services;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.Views;

public partial class BackupRestoreView : UserControl
{
    private BackupRestoreViewModel? ViewModel => DataContext as BackupRestoreViewModel;

    public BackupRestoreView()
    {
        InitializeComponent();
    }

    private async void OnBrowseBackupTargetClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var isBinary = ViewModel.ExportFormat == DatabaseExportFormat.BinaryDb;
        var ext = isBinary ? "db" : "sql";
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select Export Destination File",
            DefaultExtension = ext,
            SuggestedFileName = $"{ViewModel.DatabaseName}_dump_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SQL Dump (*.sql)") { Patterns = new[] { "*.sql" } },
                new FilePickerFileType("SQLite Database (*.db)") { Patterns = new[] { "*.db", "*.sqlite" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            ViewModel.BackupDestinationPath = file.Path.LocalPath;
        }
    }

    private async void OnBrowseRestoreSourceClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Backup / SQL Dump File to Restore",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported Database Backups (*.sql, *.db)") { Patterns = new[] { "*.sql", "*.db", "*.sqlite" } },
                new FilePickerFileType("SQL Scripts (*.sql)") { Patterns = new[] { "*.sql" } },
                new FilePickerFileType("SQLite Databases (*.db)") { Patterns = new[] { "*.db", "*.sqlite" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            ViewModel.RestoreSourcePath = files[0].Path.LocalPath;
        }
    }
}
