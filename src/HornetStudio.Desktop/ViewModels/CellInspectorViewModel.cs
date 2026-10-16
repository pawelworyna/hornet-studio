using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Services;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class CellInspectorViewModel : ObservableObject
{
    private readonly EditableRowViewModel _row;
    private readonly string _columnName;
    private readonly IClipboardService _clipboard;
    private readonly Action _onApplied;
    private readonly Action _onClose;

    public string ColumnName => _columnName;
    public string DataType { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentLength))]
    [NotifyPropertyChangedFor(nameof(ByteLength))]
    [NotifyPropertyChangedFor(nameof(LineCount))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _originalContent = string.Empty;

    [ObservableProperty]
    private bool _isJson;

    [ObservableProperty]
    private string _formatStatus = string.Empty;

    public int ContentLength => Content?.Length ?? 0;
    public int ByteLength => Content != null ? Encoding.UTF8.GetByteCount(Content) : 0;
    public int LineCount => Content != null ? (Content.Split('\n').Length) : 0;
    public bool IsModified => !string.Equals(Content, OriginalContent, StringComparison.Ordinal);

    public CellInspectorViewModel(
        EditableRowViewModel row,
        string columnName,
        string dataType,
        IClipboardService? clipboard = null,
        Action? onApplied = null,
        Action? onClose = null)
    {
        _row = row;
        _columnName = columnName;
        DataType = dataType;
        _clipboard = clipboard ?? new NullClipboardService();
        _onApplied = onApplied ?? (() => { });
        _onClose = onClose ?? (() => { });

        var val = row[columnName];
        _originalContent = val?.ToString() ?? string.Empty;
        _content = _originalContent;

        DetectJson();
    }

    private void DetectJson()
    {
        var trimmed = Content.Trim();
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                IsJson = true;
                FormatStatus = "Valid JSON";
            }
            catch
            {
                IsJson = false;
                FormatStatus = string.Empty;
            }
        }
        else
        {
            IsJson = false;
            FormatStatus = string.Empty;
        }
    }

    [RelayCommand]
    public void PrettifyJson()
    {
        try
        {
            using var doc = JsonDocument.Parse(Content);
            Content = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            IsJson = true;
            FormatStatus = "Formatted JSON";
        }
        catch (Exception ex)
        {
            FormatStatus = $"JSON Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void MinifyJson()
    {
        try
        {
            using var doc = JsonDocument.Parse(Content);
            Content = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            IsJson = true;
            FormatStatus = "Minified JSON";
        }
        catch (Exception ex)
        {
            FormatStatus = $"JSON Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CopyAsync()
    {
        await _clipboard.SetTextAsync(Content);
        FormatStatus = "Copied to clipboard!";
    }

    [RelayCommand]
    public void Apply()
    {
        _row[_columnName] = Content;
        OriginalContent = Content;
        OnPropertyChanged(nameof(IsModified));
        _onApplied();
        FormatStatus = "Applied changes to cell";
    }

    [RelayCommand]
    public void Reset()
    {
        Content = OriginalContent;
        DetectJson();
    }

    [RelayCommand]
    public void Close()
    {
        _onClose();
    }
}
