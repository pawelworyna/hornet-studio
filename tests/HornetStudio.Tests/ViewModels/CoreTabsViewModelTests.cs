using FluentAssertions;
using HornetStudio.Desktop.ViewModels;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Tests.ViewModels;

public class CoreTabsViewModelTests
{
    private class DummyTab : TabItemViewModelBase
    {
        public DummyTab(string title) { Title = title; }
    }

    [Fact]
    public void OpenTab_AddsTabAndSelectsIt()
    {
        var tabsVm = new CoreTabsViewModel();
        var tab1 = new DummyTab("Tab 1");
        var tab2 = new DummyTab("Tab 2");

        tabsVm.OpenTab(tab1);
        tabsVm.Tabs.Should().HaveCount(1);
        tabsVm.SelectedTab.Should().Be(tab1);

        tabsVm.OpenTab(tab2);
        tabsVm.Tabs.Should().HaveCount(2);
        tabsVm.SelectedTab.Should().Be(tab2);

        tabsVm.SelectTab(tab1);
        tabsVm.SelectedTab.Should().Be(tab1);

        tabsVm.CloseTab(tab1);
        tabsVm.Tabs.Should().HaveCount(1);
        tabsVm.SelectedTab.Should().Be(tab2);
    }
}
