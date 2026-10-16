using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Desktop.ViewModels;

public record FilterOperatorOption(FilterOperator Operator, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class TableFilterViewModel : ObservableObject
{
    private static readonly FilterOperatorOption[] DefaultOperatorOptions =
    [
        new(FilterOperator.Equals, "= (equals)"),
        new(FilterOperator.NotEquals, "!= (does not equal)"),
        new(FilterOperator.Like, "LIKE (contains)"),
        new(FilterOperator.NotLike, "NOT LIKE"),
        new(FilterOperator.ILike, "ILIKE (case-insensitive)"),
        new(FilterOperator.LessThan, "< (less than)"),
        new(FilterOperator.LessThanOrEqual, "<= (less or equal)"),
        new(FilterOperator.GreaterThan, "> (greater than)"),
        new(FilterOperator.GreaterThanOrEqual, ">= (greater or equal)"),
        new(FilterOperator.In, "IN (in list)"),
        new(FilterOperator.IsNull, "IS NULL"),
        new(FilterOperator.IsNotNull, "IS NOT NULL")
    ];

    [ObservableProperty]
    private string _logicalOp = "AND";

    private string _field = string.Empty;
    public string Field
    {
        get => _field;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                SetProperty(ref _field, value);
            }
            else if (string.IsNullOrEmpty(_field))
            {
                SetProperty(ref _field, value ?? string.Empty);
            }
        }
    }

    [ObservableProperty]
    private FilterOperator _operator = FilterOperator.Equals;

    [ObservableProperty]
    private FilterOperatorOption? _selectedOperatorOption;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isFirstFilter = true;

    [ObservableProperty]
    private ObservableCollection<string> _availableColumns = new();

    public ObservableCollection<FilterOperatorOption> AvailableOperators { get; } = new(DefaultOperatorOptions);

    public bool IsValueRequired => Operator != FilterOperator.IsNull && Operator != FilterOperator.IsNotNull;

    public string ValuePlaceholder => Operator switch
    {
        FilterOperator.IsNull or FilterOperator.IsNotNull => "(no value needed)",
        FilterOperator.In => "e.g. 1, 2, 3 or 'val1', 'val2'",
        FilterOperator.Like or FilterOperator.ILike or FilterOperator.NotLike => "Pattern (e.g. Acme%)",
        _ => "Enter value..."
    };

    public TableFilterViewModel()
    {
        _selectedOperatorOption = AvailableOperators[0];
    }

    partial void OnOperatorChanged(FilterOperator value)
    {
        if (SelectedOperatorOption?.Operator != value)
        {
            SelectedOperatorOption = AvailableOperators.FirstOrDefault(o => o.Operator == value) ?? AvailableOperators[0];
        }
        OnPropertyChanged(nameof(IsValueRequired));
        OnPropertyChanged(nameof(ValuePlaceholder));
    }

    partial void OnSelectedOperatorOptionChanged(FilterOperatorOption? value)
    {
        if (value != null && Operator != value.Operator)
        {
            Operator = value.Operator;
        }
    }

    [RelayCommand]
    public void ToggleLogicalOp()
    {
        LogicalOp = LogicalOp == "AND" ? "OR" : "AND";
    }

    public TableFilter ToDomainFilter()
    {
        return new TableFilter
        {
            LogicalOp = LogicalOp,
            Field = Field,
            Operator = Operator,
            Value = Value
        };
    }
}
