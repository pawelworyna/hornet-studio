using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HornetStudio.Desktop.ViewModels;

/// <summary>
/// Wraps a single table or query result row with support for dictionary-based indexer access,
/// cell-level dirty tracking, and staged update/insert/delete identification.
/// </summary>
public sealed class EditableRowViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _originalValues;
    private readonly Dictionary<string, object?> _currentValues;
    private readonly HashSet<string> _modifiedColumns = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsNewRow { get; set; }
    public bool IsMarkedForDeletion { get; private set; }

    public IReadOnlyDictionary<string, object?> OriginalValues => _originalValues;
    public IReadOnlyDictionary<string, object?> CurrentValues => _currentValues;
    public IReadOnlySet<string> ModifiedColumns => _modifiedColumns;

    public bool IsModified => IsNewRow || IsMarkedForDeletion || _modifiedColumns.Count > 0;

    public EditableRowViewModel(Dictionary<string, object?> values, bool isNew = false)
    {
        _originalValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        _currentValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        IsNewRow = isNew;
    }

    public object? this[string columnName]
    {
        get
        {
            if (_currentValues.TryGetValue(columnName, out var value))
            {
                return value;
            }
            return null;
        }
        set
        {
            var existing = this[columnName];
            if (Equals(existing, value))
                return;

            _currentValues[columnName] = value;

            if (_originalValues.TryGetValue(columnName, out var origVal) && Equals(origVal, value))
            {
                _modifiedColumns.Remove(columnName);
            }
            else
            {
                _modifiedColumns.Add(columnName);
            }

            OnPropertyChanged(BindingNotification.BindingItem);
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(StatusDescription));
        }
    }

    public void MarkForDeletion(bool delete = true)
    {
        IsMarkedForDeletion = delete;
        OnPropertyChanged(nameof(IsMarkedForDeletion));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(StatusDescription));
    }

    public void ResetChanges()
    {
        _currentValues.Clear();
        foreach (var kv in _originalValues)
        {
            _currentValues[kv.Key] = kv.Value;
        }
        _modifiedColumns.Clear();
        IsMarkedForDeletion = false;

        OnPropertyChanged(BindingNotification.BindingItem);
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsMarkedForDeletion));
        OnPropertyChanged(nameof(StatusDescription));
    }

    public bool IsColumnModified(string columnName) => _modifiedColumns.Contains(columnName);

    public string StatusDescription
    {
        get
        {
            if (IsMarkedForDeletion) return "Deleted";
            if (IsNewRow) return "New";
            if (_modifiedColumns.Count > 0) return $"{_modifiedColumns.Count} modified";
            return "Unchanged";
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static class BindingNotification
    {
        public const string BindingItem = "Item[]";
    }
}
