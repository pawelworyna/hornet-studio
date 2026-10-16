using Avalonia.Controls;
using Avalonia.Input;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Desktop.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TableOrView table } && DataContext is SidebarViewModel vm)
        {
            vm.OpenTableDataCommand.Execute(table);
        }
    }
}

