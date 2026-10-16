using HornetStudio.Core.Domain.Enums;

namespace HornetStudio.Core.Domain.Models;

public record TableOrView(
    string Name,
    string? Schema = null,
    string? Database = null,
    string EntityType = "table" // "table", "view", "materialized_view"
)
{
    public bool IsView => EntityType.Equals("view", StringComparison.OrdinalIgnoreCase) ||
                          EntityType.Equals("materialized_view", StringComparison.OrdinalIgnoreCase);
}

public class ExtendedTableColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool Nullable { get; set; } = true;
    public string? DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsAutoIncrement { get; set; }
    public bool IsGenerated { get; set; }
    public int? MaxLength { get; set; }
    public string? Comment { get; set; }
    public string? Extra { get; set; }
}

public record PrimaryKeyColumn(string ColumnName, int Position = 0);

public class TableIndex
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public List<string> Columns { get; set; } = [];
    public string? IndexType { get; set; } // BTREE, HASH, GIN, itp.
}

public class TableKey
{
    public string ConstraintName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string? ReferencedSchema { get; set; }
    public string ReferencedColumn { get; set; } = string.Empty;
    public string? OnUpdate { get; set; }
    public string? OnDelete { get; set; }
}

public class TableTrigger
{
    public string Name { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    public string Timing { get; set; } = string.Empty; // BEFORE, AFTER, INSTEAD OF
    public string? Definition { get; set; }
}

public class Routine
{
    public string Name { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public string RoutineType { get; set; } = "FUNCTION"; // FUNCTION, PROCEDURE
    public string? ReturnType { get; set; }
    public string? Definition { get; set; }
}

public class SupportedFeatures
{
    public bool CustomRoutines { get; set; } = true;
    public bool Comments { get; set; } = true;
    public bool Properties { get; set; } = true;
    public bool Partitions { get; set; }
    public bool Backups { get; set; } = true;
    public bool Restore { get; set; } = true;
    public bool Schemas { get; set; } = true;
    public bool Truncate { get; set; } = true;
    public bool ExplainPlan { get; set; } = true;
    public bool RowIdSupported { get; set; } // SQLite rowid
    public bool CtidSupported { get; set; }  // Postgres ctid
}
