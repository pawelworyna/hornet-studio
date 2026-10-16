using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Desktop.ViewModels;
using HornetStudio.Desktop.ViewModels.Tabs;
using Moq;
using Xunit;

namespace HornetStudio.Tests.ViewModels;

public sealed class CellInspectorViewModelTests
{
    [Fact]
    public void Automatically_Detects_Valid_Json()
    {
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["data"] = "{\"name\":\"Hornet\",\"active\":true}"
        });

        var vm = new CellInspectorViewModel(row, "data", "json");

        vm.IsJson.Should().BeTrue();
        vm.FormatStatus.Should().Be("Valid JSON");
        vm.ContentLength.Should().Be(31);
        vm.LineCount.Should().Be(1);
    }

    [Fact]
    public void PrettifyJson_Formats_Indented_Json()
    {
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["meta"] = "{\"tags\":[\"dev\",\"csharp\"],\"count\":2}"
        });

        var vm = new CellInspectorViewModel(row, "meta", "json");
        vm.PrettifyJson();

        vm.Content.Should().Contain("\n");
        vm.Content.Should().Contain("  \"tags\"");
        vm.IsJson.Should().BeTrue();
        vm.FormatStatus.Should().Be("Formatted JSON");
        vm.IsModified.Should().BeTrue();
    }

    [Fact]
    public void MinifyJson_Compacts_Multiline_Json()
    {
        var multiline = "{\n  \"status\": \"OK\"\n}";
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["payload"] = multiline
        });

        var vm = new CellInspectorViewModel(row, "payload", "json");
        vm.MinifyJson();

        vm.Content.Should().Be("{\"status\":\"OK\"}");
        vm.IsJson.Should().BeTrue();
        vm.FormatStatus.Should().Be("Minified JSON");
    }

    [Fact]
    public async Task CopyAsync_Copies_To_Clipboard()
    {
        var mockClipboard = new Mock<IClipboardService>();
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["description"] = "Hornet Studio Pro"
        });

        var vm = new CellInspectorViewModel(row, "description", "text", mockClipboard.Object);
        await vm.CopyAsync();

        mockClipboard.Verify(c => c.SetTextAsync("Hornet Studio Pro"), Times.Once);
        vm.FormatStatus.Should().Be("Copied to clipboard!");
    }

    [Fact]
    public void Apply_Updates_Row_And_Invokes_Callback()
    {
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["title"] = "Initial Title"
        });

        var appliedCalled = false;
        var vm = new CellInspectorViewModel(row, "title", "text", onApplied: () => appliedCalled = true);

        vm.Content = "Updated Title";
        vm.IsModified.Should().BeTrue();

        vm.Apply();

        appliedCalled.Should().BeTrue();
        row["title"].Should().Be("Updated Title");
        vm.IsModified.Should().BeFalse();
        vm.OriginalContent.Should().Be("Updated Title");
    }

    [Fact]
    public void Reset_Restores_Original_Content()
    {
        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["notes"] = "Clean"
        });

        var vm = new CellInspectorViewModel(row, "notes", "text");
        vm.Content = "Dirty text";
        vm.IsModified.Should().BeTrue();

        vm.Reset();

        vm.Content.Should().Be("Clean");
        vm.IsModified.Should().BeFalse();
    }

    [Fact]
    public void TableDataViewModel_InspectCell_Opens_Inspector_With_Correct_Data()
    {
        var mockClient = new Mock<IDatabaseClient>();
        var mockExport = new Mock<IExportImportService>();
        var table = new TableOrView("logs", "public");

        var tvm = new TableDataViewModel(mockClient.Object, mockExport.Object, table);
        tvm.Columns.Add(new ExtendedTableColumn { ColumnName = "details", DataType = "jsonb" });

        var row = new EditableRowViewModel(new Dictionary<string, object?>
        {
            ["details"] = "{\"event\":\"login\"}"
        });

        tvm.InspectCell(row, "details");

        tvm.IsCellInspectorOpen.Should().BeTrue();
        tvm.CellInspector.Should().NotBeNull();
        tvm.CellInspector!.ColumnName.Should().Be("details");
        tvm.CellInspector!.DataType.Should().Be("jsonb");
        tvm.CellInspector!.Content.Should().Be("{\"event\":\"login\"}");

        // Closing the inspector
        tvm.CloseCellInspector();
        tvm.IsCellInspectorOpen.Should().BeFalse();
        tvm.CellInspector.Should().BeNull();
    }
}
