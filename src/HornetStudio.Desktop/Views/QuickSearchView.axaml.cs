using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Desktop.Views;

public partial class QuickSearchView : UserControl
{
    private TextBox? _searchBox;

    public QuickSearchView()
    {
        InitializeComponent();
        _searchBox = this.FindControl<TextBox>("SearchBox");
        if (_searchBox != null)
        {
            _searchBox.KeyDown += OnSearchBoxKeyDown;
        }

        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _searchBox?.Focus();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickSearchViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                vm.SelectNextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Up:
                vm.SelectPreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                var isAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                vm.ConfirmCommand.Execute(isAlt);
                e.Handled = true;
                break;
        }
    }

    private void OnListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not QuickSearchViewModel vm) return;
        vm.ConfirmCommand.Execute(false);
    }
}
