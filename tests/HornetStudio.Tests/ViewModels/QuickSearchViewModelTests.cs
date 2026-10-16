using System;
using System.Collections.Generic;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Desktop.ViewModels;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class QuickSearchViewModelTests
{
    [Fact]
    public void Initializes_With_All_Provided_Items()
    {
        var tables = new List<TableOrView>
        {
            new("users", "public", null, "table"),
            new("v_active_users", "public", null, "view")
        };

        var queries = new List<FavoriteQueryEntity>
        {
            new() { Id = Guid.NewGuid(), Title = "Monthly Report", QueryText = "SELECT 1;" }
        };

        var history = new List<QueryHistoryEntity>
        {
            new() { Id = Guid.NewGuid(), QueryText = "SELECT * FROM users;", ExecutedAt = DateTime.UtcNow }
        };

        var vm = new QuickSearchViewModel(
            tables,
            queries,
            history,
            openTableData: _ => { },
            openTableProperties: _ => { },
            openQuery: (_, _) => { },
            closeModal: () => { });

        Assert.Equal(4, vm.FilteredItems.Count);
        Assert.NotNull(vm.SelectedItem);
        Assert.Equal("users", vm.SelectedItem.Title);
    }

    [Fact]
    public void FilterText_Matches_Tables_And_Queries()
    {
        var tables = new List<TableOrView>
        {
            new("customers", "public", null, "table"),
            new("orders", "public", null, "table")
        };

        var queries = new List<FavoriteQueryEntity>
        {
            new() { Id = Guid.NewGuid(), Title = "Find Customers", QueryText = "SELECT * FROM customers;" }
        };

        var vm = new QuickSearchViewModel(
            tables,
            queries,
            [],
            openTableData: _ => { },
            openTableProperties: _ => { },
            openQuery: (_, _) => { },
            closeModal: () => { });

        vm.SearchText = "cust";

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.Contains("cust", item.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectNext_And_SelectPrevious_Navigate_List()
    {
        var tables = new List<TableOrView>
        {
            new("table_a", "public", null, "table"),
            new("table_b", "public", null, "table"),
            new("table_c", "public", null, "table")
        };

        var vm = new QuickSearchViewModel(
            tables,
            [],
            [],
            openTableData: _ => { },
            openTableProperties: _ => { },
            openQuery: (_, _) => { },
            closeModal: () => { });

        Assert.Equal("table_a", vm.SelectedItem?.Title);

        vm.SelectNextCommand.Execute(null);
        Assert.Equal("table_b", vm.SelectedItem?.Title);

        vm.SelectNextCommand.Execute(null);
        Assert.Equal("table_c", vm.SelectedItem?.Title);

        // Does not go beyond bounds
        vm.SelectNextCommand.Execute(null);
        Assert.Equal("table_c", vm.SelectedItem?.Title);

        vm.SelectPreviousCommand.Execute(null);
        Assert.Equal("table_b", vm.SelectedItem?.Title);
    }

    [Fact]
    public void Confirm_Executes_Correct_Callback_For_TableData_And_Properties()
    {
        var table = new TableOrView("products", "store", null, "table");
        TableOrView? dataOpened = null;
        TableOrView? propsOpened = null;
        var modalClosed = false;

        var vm = new QuickSearchViewModel(
            new[] { table },
            [],
            [],
            openTableData: t => dataOpened = t,
            openTableProperties: t => propsOpened = t,
            openQuery: (_, _) => { },
            closeModal: () => modalClosed = true);

        // Standard Enter -> Open Table Data
        vm.Confirm(alt: false);
        Assert.True(modalClosed);
        Assert.Same(table, dataOpened);
        Assert.Null(propsOpened);

        // Alt + Enter -> Open Table Properties
        modalClosed = false;
        vm.Confirm(alt: true);
        Assert.True(modalClosed);
        Assert.Same(table, propsOpened);
    }

    [Fact]
    public void Confirm_Executes_Correct_Callback_For_SavedQuery()
    {
        var query = new FavoriteQueryEntity { Id = Guid.NewGuid(), Title = "High Value", QueryText = "SELECT * FROM orders WHERE val > 1000;" };
        string? openedTitle = null;
        string? openedSql = null;
        var modalClosed = false;

        var vm = new QuickSearchViewModel(
            [],
            new[] { query },
            [],
            openTableData: _ => { },
            openTableProperties: _ => { },
            openQuery: (t, s) => { openedTitle = t; openedSql = s; },
            closeModal: () => modalClosed = true);

        vm.Confirm(alt: false);

        Assert.True(modalClosed);
        Assert.Equal("High Value", openedTitle);
        Assert.Equal(query.QueryText, openedSql);
    }
}
