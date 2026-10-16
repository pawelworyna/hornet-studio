using FluentAssertions;
using HornetStudio.Desktop.ViewModels;

namespace HornetStudio.Tests.ViewModels;

public class EditableRowViewModelTests
{
    [Fact]
    public void Indexer_ReturnsAndModifiesValues_AndTracksDirtyState()
    {
        var initial = new Dictionary<string, object?>
        {
            ["id"] = 101,
            ["name"] = "Alice",
            ["role"] = "Dev"
        };

        var row = new EditableRowViewModel(initial);
        row.IsModified.Should().BeFalse();
        row.StatusDescription.Should().Be("Unchanged");

        // Modify a cell
        row["name"] = "Bob";
        row.IsModified.Should().BeTrue();
        row.IsColumnModified("name").Should().BeTrue();
        row.IsColumnModified("role").Should().BeFalse();
        row.StatusDescription.Should().Be("1 modified");

        // Revert back to original value
        row["name"] = "Alice";
        row.IsModified.Should().BeFalse();
        row.IsColumnModified("name").Should().BeFalse();
        row.StatusDescription.Should().Be("Unchanged");
    }

    [Fact]
    public void MarkForDeletion_SetsFlagsCorrectly()
    {
        var initial = new Dictionary<string, object?> { ["id"] = 1 };
        var row = new EditableRowViewModel(initial);

        row.MarkForDeletion(true);
        row.IsMarkedForDeletion.Should().BeTrue();
        row.IsModified.Should().BeTrue();
        row.StatusDescription.Should().Be("Deleted");

        row.ResetChanges();
        row.IsMarkedForDeletion.Should().BeFalse();
        row.IsModified.Should().BeFalse();
    }

    [Fact]
    public void NewRow_HasIsNewRowFlag_AndIsModified()
    {
        var dict = new Dictionary<string, object?> { ["name"] = "Charlie" };
        var row = new EditableRowViewModel(dict, isNew: true);

        row.IsNewRow.Should().BeTrue();
        row.IsModified.Should().BeTrue();
        row.StatusDescription.Should().Be("New");
    }
}
