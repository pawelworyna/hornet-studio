using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Dialects;

public abstract class ChangeBuilderBase
{
    public string Table { get; }
    public string? Schema { get; }

    protected ChangeBuilderBase(string table, string? schema = null)
    {
        Table = table;
        Schema = schema;
    }

    public virtual string WrapIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
    public virtual string EscapeLiteral(string value) => value.Replace("'", "''");

    public virtual string TableName => string.IsNullOrEmpty(Schema)
        ? WrapIdentifier(Table)
        : $"{WrapIdentifier(Schema)}.{WrapIdentifier(Table)}";

    public virtual string AddColumn(ExtendedTableColumn col)
    {
        var nullability = col.Nullable ? "NULL" : "NOT NULL";
        var dflt = col.DefaultValue != null ? $" DEFAULT {col.DefaultValue}" : "";
        return $"ALTER TABLE {TableName} ADD COLUMN {WrapIdentifier(col.ColumnName)} {col.DataType} {nullability}{dflt}";
    }

    public virtual string DropColumn(string columnName)
    {
        return $"ALTER TABLE {TableName} DROP COLUMN {WrapIdentifier(columnName)}";
    }

    public virtual string RenameColumn(string oldName, string newName)
    {
        return $"ALTER TABLE {TableName} RENAME COLUMN {WrapIdentifier(oldName)} TO {WrapIdentifier(newName)}";
    }

    public virtual string CreateIndex(string indexName, IEnumerable<string> columns, bool isUnique)
    {
        var unique = isUnique ? "UNIQUE " : "";
        var colList = string.Join(", ", columns.Select(WrapIdentifier));
        return $"CREATE {unique}INDEX {WrapIdentifier(indexName)} ON {TableName} ({colList})";
    }

    public virtual string DropIndex(string indexName)
    {
        return $"DROP INDEX {WrapIdentifier(indexName)}";
    }

    public virtual string CreateTable(IEnumerable<ExtendedTableColumn> columns)
    {
        var colDefs = new List<string>();
        var pks = new List<string>();

        foreach (var c in columns)
        {
            var nullability = c.Nullable ? "NULL" : "NOT NULL";
            var dflt = c.DefaultValue != null ? $" DEFAULT {c.DefaultValue}" : "";
            colDefs.Add($"{WrapIdentifier(c.ColumnName)} {c.DataType} {nullability}{dflt}");
            if (c.IsPrimaryKey) pks.Add(WrapIdentifier(c.ColumnName));
        }

        if (pks.Count > 0)
        {
            colDefs.Add($"PRIMARY KEY ({string.Join(", ", pks)})");
        }

        return $"CREATE TABLE {TableName} (\n  {string.Join(",\n  ", colDefs)}\n)";
    }
}

public class SqliteChangeBuilder : ChangeBuilderBase
{
    public SqliteChangeBuilder(string table, string? schema = null) : base(table, schema) { }
    // SQLite doesn't require "COLUMN" in ADD COLUMN but supports it
}

public class PostgreSqlChangeBuilder : ChangeBuilderBase
{
    public PostgreSqlChangeBuilder(string table, string? schema = null) : base(table, schema) { }
    public override string DropIndex(string indexName) => string.IsNullOrEmpty(Schema)
        ? $"DROP INDEX {WrapIdentifier(indexName)}"
        : $"DROP INDEX {WrapIdentifier(Schema)}.{WrapIdentifier(indexName)}";
}

public class MySqlChangeBuilder : ChangeBuilderBase
{
    public MySqlChangeBuilder(string table, string? schema = null) : base(table, schema) { }
    public override string WrapIdentifier(string name) => $"`{name.Replace("`", "``")}`";
    public override string DropIndex(string indexName) => $"DROP INDEX {WrapIdentifier(indexName)} ON {TableName}";
}

public class SqlServerChangeBuilder : ChangeBuilderBase
{
    public SqlServerChangeBuilder(string table, string? schema = null) : base(table, schema) { }
    public override string WrapIdentifier(string name) => $"[{name.Replace("]", "]]")}]";
    public override string DropIndex(string indexName) => $"DROP INDEX {WrapIdentifier(indexName)} ON {TableName}";
}

public class FirebirdChangeBuilder : ChangeBuilderBase
{
    public FirebirdChangeBuilder(string table, string? schema = null) : base(table, schema) { }

    public override string AddColumn(ExtendedTableColumn col)
    {
        var nullability = col.Nullable ? "" : " NOT NULL";
        var dflt = col.DefaultValue != null ? $" DEFAULT {col.DefaultValue}" : "";
        return $"ALTER TABLE {TableName} ADD {WrapIdentifier(col.ColumnName)} {col.DataType}{nullability}{dflt}";
    }

    public override string DropColumn(string columnName)
    {
        return $"ALTER TABLE {TableName} DROP {WrapIdentifier(columnName)}";
    }

    public override string RenameColumn(string oldName, string newName)
    {
        return $"ALTER TABLE {TableName} ALTER COLUMN {WrapIdentifier(oldName)} TO {WrapIdentifier(newName)}";
    }

    public override string DropIndex(string indexName) => $"DROP INDEX {WrapIdentifier(indexName)}";
}

