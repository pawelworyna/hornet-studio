using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage.Entities;

namespace HornetStudio.Desktop.ViewModels;

public enum QuickSearchItemType
{
    Table,
    View,
    Query,
    History
}

public sealed class QuickSearchItemViewModel
{
    public string Title { get; }
    public string Subtitle { get; }
    public QuickSearchItemType ItemType { get; }
    public string IconText { get; }
    public string BadgeColor { get; }
    public object Payload { get; }

    public QuickSearchItemViewModel(string title, string subtitle, QuickSearchItemType itemType, object payload)
    {
        Title = title;
        Subtitle = subtitle;
        ItemType = itemType;
        Payload = payload;

        IconText = itemType switch
        {
            QuickSearchItemType.Table => "📄",
            QuickSearchItemType.View => "👁️",
            QuickSearchItemType.Query => "⭐",
            QuickSearchItemType.History => "🕒",
            _ => "•"
        };

        BadgeColor = itemType switch
        {
            QuickSearchItemType.Table => "#10B981",
            QuickSearchItemType.View => "#3B82F6",
            QuickSearchItemType.Query => "#F59E0B",
            QuickSearchItemType.History => "#94A3B8",
            _ => "#64748B"
        };
    }
}

public sealed partial class QuickSearchViewModel : ObservableObject
{
    private readonly List<QuickSearchItemViewModel> _allItems = [];
    private readonly Action<TableOrView> _openTableData;
    private readonly Action<TableOrView> _openTableProperties;
    private readonly Action<string, string> _openQuery;
    private readonly Action _closeModal;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private QuickSearchItemViewModel? _selectedItem;

    public ObservableCollection<QuickSearchItemViewModel> FilteredItems { get; } = [];

    public QuickSearchViewModel(
        IEnumerable<TableOrView> tablesAndViews,
        IEnumerable<FavoriteQueryEntity> savedQueries,
        IEnumerable<QueryHistoryEntity> queryHistory,
        Action<TableOrView> openTableData,
        Action<TableOrView> openTableProperties,
        Action<string, string> openQuery,
        Action closeModal)
    {
        _openTableData = openTableData;
        _openTableProperties = openTableProperties;
        _openQuery = openQuery;
        _closeModal = closeModal;

        // 1. Tables and Views
        foreach (var tv in tablesAndViews)
        {
            var type = tv.IsView ? QuickSearchItemType.View : QuickSearchItemType.Table;
            var sub = tv.Schema != null ? $"{type} • {tv.Schema}.{tv.Name}" : $"{type}";
            _allItems.Add(new QuickSearchItemViewModel(tv.Name, sub, type, tv));
        }

        // 2. Saved Queries
        foreach (var q in savedQueries)
        {
            _allItems.Add(new QuickSearchItemViewModel(q.Title, "Saved Query", QuickSearchItemType.Query, q));
        }

        // 3. Query History (top 15 unique)
        var seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in queryHistory.Take(15))
        {
            var snippet = h.QueryText.Trim().Replace("\r\n", " ").Replace("\n", " ");
            if (snippet.Length > 60) snippet = snippet[..57] + "...";
            if (seenQueries.Add(snippet))
            {
                _allItems.Add(new QuickSearchItemViewModel(snippet, $"History • {h.ExecutedAt:g}", QuickSearchItemType.History, h));
            }
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        IEnumerable<QuickSearchItemViewModel> matches;
        if (string.IsNullOrEmpty(query))
        {
            // Show all items (up to 30)
            matches = _allItems.Take(30);
        }
        else
        {
            matches = _allItems
                .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(30);
        }

        foreach (var m in matches)
        {
            FilteredItems.Add(m);
        }

        SelectedItem = FilteredItems.FirstOrDefault();
    }

    [RelayCommand]
    public void SelectNext()
    {
        if (FilteredItems.Count == 0) return;
        var idx = SelectedItem != null ? FilteredItems.IndexOf(SelectedItem) : -1;
        if (idx < FilteredItems.Count - 1)
        {
            SelectedItem = FilteredItems[idx + 1];
        }
    }

    [RelayCommand]
    public void SelectPrevious()
    {
        if (FilteredItems.Count == 0) return;
        var idx = SelectedItem != null ? FilteredItems.IndexOf(SelectedItem) : -1;
        if (idx > 0)
        {
            SelectedItem = FilteredItems[idx - 1];
        }
    }

    [RelayCommand]
    public void Confirm(bool alt = false)
    {
        if (SelectedItem == null) return;

        var payload = SelectedItem.Payload;
        _closeModal();

        switch (SelectedItem.ItemType)
        {
            case QuickSearchItemType.Table:
            case QuickSearchItemType.View:
                if (payload is TableOrView tv)
                {
                    if (alt)
                    {
                        _openTableProperties(tv);
                    }
                    else
                    {
                        _openTableData(tv);
                    }
                }
                break;

            case QuickSearchItemType.Query:
                if (payload is FavoriteQueryEntity fq)
                {
                    _openQuery(fq.Title, fq.QueryText);
                }
                break;

            case QuickSearchItemType.History:
                if (payload is QueryHistoryEntity qh)
                {
                    _openQuery($"History: {qh.ExecutedAt:HH:mm:ss}", qh.QueryText);
                }
                break;
        }
    }

    [RelayCommand]
    public void Close()
    {
        _closeModal();
    }
}
