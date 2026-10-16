using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Desktop.ViewModels.Tabs;
using Moq;

namespace HornetStudio.Tests.ViewModels;

public class TableDataViewModelTests
{
    [Fact]
    public void CollectChanges_CapturesInsertsUpdatesAndDeletesCorrectly()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();
        var table = new TableOrView("orders", "public");

        var vm = new TableDataViewModel(mockClient.Object, mockExport.Object, table)
        {
            RowIdMode = RowIdentifierMode.RowId,
            IsNoPkMode = true
        };

        // Add columns
        vm.Columns.Add(new ExtendedTableColumn { ColumnName = "product", DataType = "text" });
        vm.Columns.Add(new ExtendedTableColumn { ColumnName = "qty", DataType = "int" });

        // Existing row with SQLite _hornet_rowid
        var existingRow = new HornetStudio.Desktop.ViewModels.EditableRowViewModel(new Dictionary<string, object?>
        {
            ["_hornet_rowid"] = 42L,
            ["product"] = "Drone",
            ["qty"] = 1
        });
        vm.Rows.Add(existingRow);

        // Modify the existing row
        existingRow["qty"] = 5;

        // Add a new row
        vm.AddNewRow();
        var newRow = vm.Rows[0];
        newRow["product"] = "Propeller";
        newRow["qty"] = 10;

        vm.HasStagedChanges.Should().BeTrue();
        vm.StagedChangesSummary.Should().Contain("1 to insert");
        vm.StagedChangesSummary.Should().Contain("1 to update");

        var changes = vm.CollectChanges();
        changes.Table.Should().Be("orders");
        changes.Inserts.Should().HaveCount(1);
        changes.Inserts[0].Values["product"].Should().Be("Propeller");

        changes.Updates.Should().HaveCount(1);
        changes.Updates[0].RowId.Should().Be(42L);
        changes.Updates[0].UpdatedValues["qty"].Should().Be(5);

        // Discard
        vm.DiscardChanges();
        vm.HasStagedChanges.Should().BeFalse();
        vm.Rows.Should().HaveCount(1);
        vm.Rows[0]["qty"].Should().Be(1);
    }

    [Fact]
    public void TableFilterViewModel_OperatorAndLogicalOp_WorkCorrectly()
    {
        var filter = new HornetStudio.Desktop.ViewModels.TableFilterViewModel
        {
            Field = "status",
            Operator = FilterOperator.Equals,
            Value = "ACTIVE"
        };

        filter.IsValueRequired.Should().BeTrue();
        filter.LogicalOp.Should().Be("AND");

        // Toggle AND / OR
        filter.ToggleLogicalOp();
        filter.LogicalOp.Should().Be("OR");

        // Switch to IS NULL
        filter.Operator = FilterOperator.IsNull;
        filter.IsValueRequired.Should().BeFalse();
        filter.ValuePlaceholder.Should().Contain("no value");

        var domain = filter.ToDomainFilter();
        domain.LogicalOp.Should().Be("OR");
        domain.Field.Should().Be("status");
        domain.Operator.Should().Be(FilterOperator.IsNull);
    }

    [Fact]
    public async Task TableDataViewModel_FilterOperations_TriggerExpectedOptions()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();
        var table = new TableOrView("products", "public");

        SelectTopOptions? capturedOptions = null;
        mockClient.Setup(c => c.SelectTopAsync(It.IsAny<SelectTopOptions>(), It.IsAny<CancellationToken>()))
            .Callback<SelectTopOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .ReturnsAsync(new TableResult
            {
                Columns = [new ExtendedTableColumn { ColumnName = "id" }, new ExtendedTableColumn { ColumnName = "price" }],
                Rows = [],
                TotalCount = 0
            });

        var vm = new TableDataViewModel(mockClient.Object, mockExport.Object, table);

        // 1. Initial LoadData creates a filter row
        await vm.LoadDataAsync();
        vm.AvailableColumnNames.Should().Contain("id");
        vm.AvailableColumnNames.Should().Contain("price");
        vm.Filters.Should().HaveCount(1);
        vm.Filters[0].IsFirstFilter.Should().BeTrue();

        // 2. Add second filter
        vm.AddFilter();
        vm.Filters.Should().HaveCount(2);
        vm.Filters[0].IsFirstFilter.Should().BeTrue();
        vm.Filters[1].IsFirstFilter.Should().BeFalse();

        // Configure filter
        vm.Filters[0].Field = "price";
        vm.Filters[0].Operator = FilterOperator.GreaterThan;
        vm.Filters[0].Value = "100";

        // Apply
        await vm.ApplyFilterAsync();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Filters.Should().HaveCount(1);
        capturedOptions.Filters[0].Field.Should().Be("price");
        capturedOptions.Filters[0].Value.Should().Be("100");
        vm.HasActiveFilter.Should().BeTrue();
        vm.ActiveFilterSummary.Should().Contain("1 filter(s) active");

        // Verify Field does not disappear after applying filter!
        vm.Filters[0].Field.Should().Be("price");

        // 3. Raw Filter mode
        vm.IsRawFilterMode = true;
        vm.RawFilterText = "price > 500 AND id < 10";
        await vm.ApplyFilterAsync();
        capturedOptions.RawWhereClause.Should().Be("price > 500 AND id < 10");
        vm.ActiveFilterSummary.Should().Contain("Raw:");

        // 4. Clear filter
        await vm.ClearFilterAsync();
        vm.RawFilterText.Should().BeEmpty();
        vm.HasActiveFilter.Should().BeFalse();

        // 5. Quick filter by cell
        await vm.QuickFilterAsync("price", 250.0);
        capturedOptions.Filters.Should().HaveCount(1);
        capturedOptions.Filters[0].Field.Should().Be("price");
        capturedOptions.Filters[0].Operator.Should().Be(FilterOperator.Equals);
        capturedOptions.Filters[0].Value.Should().Be("250");
    }
}

