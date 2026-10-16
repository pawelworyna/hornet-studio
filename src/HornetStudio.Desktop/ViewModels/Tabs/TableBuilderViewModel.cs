using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Desktop.ViewModels.Tabs;

public sealed partial class TableColumnDraftViewModel : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _dataType = "VARCHAR(255)";

    [ObservableProperty]
    private bool _isPrimaryKey;

    [ObservableProperty]
    private bool _isNullable = true;

    [ObservableProperty]
    private bool _isAutoIncrement;

    [ObservableProperty]
    private string _defaultValue = string.Empty;

    public TableColumnDraftViewModel(Action onChanged)
    {
        _onChanged = onChanged;
    }

    partial void OnNameChanged(string value) => _onChanged();
    partial void OnDataTypeChanged(string value) => _onChanged();
    partial void OnIsPrimaryKeyChanged(bool value)
    {
        if (value) IsNullable = false;
        _onChanged();
    }
    partial void OnIsNullableChanged(bool value) => _onChanged();
    partial void OnIsAutoIncrementChanged(bool value)
    {
        if (value) IsPrimaryKey = true;
        _onChanged();
    }
    partial void OnDefaultValueChanged(string value) => _onChanged();
}

public sealed partial class TableBuilderViewModel : TabItemViewModelBase
{
    private readonly IDatabaseClient _client;
    private readonly Func<Task> _onTableCreated;

    [ObservableProperty]
    private string _tableName = "new_table";

    [ObservableProperty]
    private string? _schema;

    [ObservableProperty]
    private string _generatedDdl = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<TableColumnDraftViewModel> Columns { get; } = new();

    public static readonly string[] CommonDataTypes =
    [
        "INT",
        "BIGINT",
        "VARCHAR(255)",
        "VARCHAR(100)",
        "TEXT",
        "BOOLEAN",
        "DECIMAL(10,2)",
        "NUMERIC",
        "TIMESTAMP",
        "TIMESTAMPTZ",
        "DATE",
        "UUID",
        "JSON",
        "JSONB",
        "BLOB",
        "BYTEA"
    ];

    public TableBuilderViewModel(
        IDatabaseClient client,
        string? defaultSchema,
        Func<Task> onTableCreated)
    {
        _client = client;
        _schema = defaultSchema;
        _onTableCreated = onTableCreated;

        Title = "Create Table (Builder)";
        IconKey = "IconAdd";

        // Add a default ID primary key column
        var idCol = new TableColumnDraftViewModel(RegenerateDdl)
        {
            Name = "id",
            DataType = client.ConnectionType == ConnectionType.PostgreSql ? "BIGINT" : "INT",
            IsPrimaryKey = true,
            IsAutoIncrement = true,
            IsNullable = false
        };
        Columns.Add(idCol);

        // Add a default name column
        var nameCol = new TableColumnDraftViewModel(RegenerateDdl)
        {
            Name = "name",
            DataType = "VARCHAR(255)",
            IsNullable = false
        };
        Columns.Add(nameCol);

        // Add created_at timestamp
        var createdCol = new TableColumnDraftViewModel(RegenerateDdl)
        {
            Name = "created_at",
            DataType = "TIMESTAMP",
            DefaultValue = "CURRENT_TIMESTAMP",
            IsNullable = false
        };
        Columns.Add(createdCol);

        RegenerateDdl();
    }

    partial void OnTableNameChanged(string value) => RegenerateDdl();
    partial void OnSchemaChanged(string? value) => RegenerateDdl();

    [RelayCommand]
    public void AddColumn()
    {
        var col = new TableColumnDraftViewModel(RegenerateDdl)
        {
            Name = $"column_{Columns.Count + 1}",
            DataType = "VARCHAR(255)",
            IsNullable = true
        };
        Columns.Add(col);
        RegenerateDdl();
    }

    [RelayCommand]
    public void RemoveColumn(TableColumnDraftViewModel? col)
    {
        if (col != null && Columns.Count > 1)
        {
            Columns.Remove(col);
            RegenerateDdl();
        }
    }

    [RelayCommand]
    public void MoveColumnUp(TableColumnDraftViewModel? col)
    {
        if (col == null) return;
        var idx = Columns.IndexOf(col);
        if (idx > 0)
        {
            Columns.Move(idx, idx - 1);
            RegenerateDdl();
        }
    }

    [RelayCommand]
    public void MoveColumnDown(TableColumnDraftViewModel? col)
    {
        if (col == null) return;
        var idx = Columns.IndexOf(col);
        if (idx < Columns.Count - 1)
        {
            Columns.Move(idx, idx + 1);
            RegenerateDdl();
        }
    }

    public void RegenerateDdl()
    {
        var sb = new StringBuilder();
        var cleanTable = string.IsNullOrWhiteSpace(TableName) ? "new_table" : TableName.Trim();

        if (_client.ConnectionType == ConnectionType.MongoDb)
        {
            GeneratedDdl = $"db.createCollection(\"{cleanTable}\")";
            return;
        }

        string qualifiedTable;
        if (!string.IsNullOrWhiteSpace(Schema))
        {
            qualifiedTable = $"{_client.WrapIdentifier(Schema)}.{_client.WrapIdentifier(cleanTable)}";
        }
        else
        {
            qualifiedTable = _client.WrapIdentifier(cleanTable);
        }

        sb.AppendLine($"CREATE TABLE {qualifiedTable} (");

        var colDefs = new List<string>();
        var pkCols = Columns.Where(c => c.IsPrimaryKey && !c.IsAutoIncrement).ToList();
        var autoPk = Columns.FirstOrDefault(c => c.IsPrimaryKey && c.IsAutoIncrement);

        for (var i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];
            var colName = string.IsNullOrWhiteSpace(col.Name) ? $"column_{i + 1}" : col.Name.Trim();
            var colType = string.IsNullOrWhiteSpace(col.DataType) ? "VARCHAR(255)" : col.DataType.Trim();

            var line = new StringBuilder();
            line.Append($"  {_client.WrapIdentifier(colName)} ");

            // Type handling per engine
            if (col.IsAutoIncrement)
            {
                if (_client.ConnectionType == ConnectionType.PostgreSql)
                {
                    line.Append("BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY");
                }
                else if (_client.ConnectionType == ConnectionType.Sqlite)
                {
                    line.Append("INTEGER PRIMARY KEY AUTOINCREMENT");
                }
                else if (_client.ConnectionType == ConnectionType.SqlServer)
                {
                    line.Append("INT IDENTITY(1,1) PRIMARY KEY");
                }
                else if (_client.ConnectionType == ConnectionType.MySql)
                {
                    line.Append("INT AUTO_INCREMENT PRIMARY KEY");
                }
                else
                {
                    line.Append($"{colType} PRIMARY KEY");
                }
            }
            else
            {
                line.Append(colType);

                if (!col.IsNullable)
                {
                    line.Append(" NOT NULL");
                }

                if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                {
                    line.Append($" DEFAULT {col.DefaultValue}");
                }
            }

            colDefs.Add(line.ToString());
        }

        // Composite or non-auto PK constraint
        if (autoPk == null && pkCols.Count > 0)
        {
            var pkNames = string.Join(", ", pkCols.Select(c => _client.WrapIdentifier(c.Name)));
            colDefs.Add($"  PRIMARY KEY ({pkNames})");
        }

        sb.AppendLine(string.Join(",\n", colDefs));
        sb.Append(");");

        GeneratedDdl = sb.ToString();
    }

    [RelayCommand]
    public async Task CreateTableAsync()
    {
        if (string.IsNullOrWhiteSpace(TableName) || Columns.Count == 0 || IsCreating)
            return;

        IsCreating = true;
        StatusMessage = "Creating table...";
        ErrorMessage = null;

        try
        {
            await _client.ExecuteQueryAsync(GeneratedDdl);
            StatusMessage = $"✓ Table '{TableName}' created successfully!";
            await _onTableCreated();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create table: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsCreating = false;
        }
    }
}
