using HornetStudio.Core.Domain.Enums;

namespace HornetStudio.Core.Domain.Models;

public class QueryResultColumn
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Ordinal { get; set; }
}

public class QueryResult
{
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public List<QueryResultColumn> Columns { get; set; } = [];
    public List<object?[]> Rows { get; set; } = [];
    public long AffectedRows { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ExecutedSql { get; set; }
}

public class TableFilter
{
    public string LogicalOp { get; set; } = "AND";
    public string Field { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;
    public object? Value { get; set; }
}

public class OrderBy
{
    public string Field { get; set; } = string.Empty;
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}

public class SelectTopOptions
{
    public string Table { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 100;
    public List<OrderBy> OrderBy { get; set; } = [];
    public List<TableFilter> Filters { get; set; } = [];
    public string? RawWhereClause { get; set; }
    public List<string>? SelectColumns { get; set; }
}

public class TableResult
{
    public List<ExtendedTableColumn> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public long TotalCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public List<string> PrimaryKeys { get; set; } = [];
    public RowIdentifierMode IdentifierMode { get; set; } = RowIdentifierMode.PrimaryKey;
    public TimeSpan Duration { get; set; }
}

public class TableInsert
{
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class TableUpdate
{
    public Dictionary<string, object?> OriginalRow { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> UpdatedValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long? RowId { get; set; }
    public string? Ctid { get; set; }
}

public class TableDelete
{
    public Dictionary<string, object?> OriginalRow { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long? RowId { get; set; }
    public string? Ctid { get; set; }
}

public class TableChanges
{
    public string Table { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public List<TableInsert> Inserts { get; set; } = [];
    public List<TableUpdate> Updates { get; set; } = [];
    public List<TableDelete> Deletes { get; set; } = [];
    public RowIdentifierMode IdentifierMode { get; set; } = RowIdentifierMode.PrimaryKey;
}

public class TableUpdateResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public int AffectedRows { get; set; }
}
