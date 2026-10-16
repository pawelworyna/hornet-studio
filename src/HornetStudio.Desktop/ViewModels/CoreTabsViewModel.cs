using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class CoreTabsViewModel : ObservableObject
{
    [ObservableProperty]
    private TabItemViewModelBase? _selectedTab;

    public ObservableCollection<TabItemViewModelBase> Tabs { get; } = new();

    public event Action? TabsChanged;

    public void OpenTab(TabItemViewModelBase tab)
    {
        // If already open, switch to it
        foreach (var existing in Tabs)
        {
            if (existing is TableDataViewModel existingTd && tab is TableDataViewModel newTd
                && existingTd.Table.Name == newTd.Table.Name && existingTd.Table.Schema == newTd.Table.Schema)
            {
                SelectTab(existingTd);
                return;
            }

            if (existing is TablePropertiesViewModel existingTp && tab is TablePropertiesViewModel newTp
                && existingTp.Table.Name == newTp.Table.Name && existingTp.Table.Schema == newTp.Table.Schema)
            {
                SelectTab(existingTp);
                return;
            }
        }

        Tabs.Add(tab);
        SelectTab(tab);
        TabsChanged?.Invoke();
    }

    [RelayCommand]
    public void SelectTab(TabItemViewModelBase? tab)
    {
        foreach (var t in Tabs)
        {
            t.IsSelected = (t == tab);
        }
        SelectedTab = tab;
        TabsChanged?.Invoke();
    }

    [RelayCommand]
    public void CloseTab(TabItemViewModelBase tab)
    {
        if (!tab.CanClose())
            return;

        tab.OnClosed();
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (SelectedTab == tab)
        {
            if (Tabs.Count > 0)
            {
                var nextIndex = index >= Tabs.Count ? Tabs.Count - 1 : index;
                SelectTab(Tabs[nextIndex]);
            }
            else
            {
                SelectTab(null);
            }
        }
        TabsChanged?.Invoke();
    }
}
