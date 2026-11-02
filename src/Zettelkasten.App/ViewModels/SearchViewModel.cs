using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly ISearchService _search;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly Debouncer _debounce;
    private int _requestVersion;
    private bool _updating;

    public SearchViewModel(ISearchService search, TimeProvider time, ILogger<SearchViewModel> logger)
    {
        _search = search;
        _logger = logger;
        _debounce = new Debouncer(TimeSpan.FromMilliseconds(250), time);
    }

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SearchResult? _selected;

    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>The most recently scheduled search (exposed for tests).</summary>
    internal Task PendingSearch { get; private set; } = Task.CompletedTask;

    public event EventHandler<string>? OpenRequested;

    partial void OnQueryChanged(string value) => PendingSearch = _debounce.Trigger(RefreshAsync);

    partial void OnSelectedChanged(SearchResult? value)
    {
        if (!_updating && value is not null) OpenRequested?.Invoke(this, value.Note.Id);
    }

    public async Task RefreshAsync()
    {
        var version = ++_requestVersion;
        IReadOnlyList<SearchResult> results;
        try
        {
            results = await _search.SearchAsync(Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for {Query}", Query);
            return;
        }
        if (version != _requestVersion) return; // a newer search is running

        _updating = true;
        try
        {
            var selectedId = Selected?.Note.Id;
            Results.Clear();
            foreach (var r in results) Results.Add(r);
            Selected = Results.FirstOrDefault(r => r.Note.Id == selectedId);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Highlights the open note without triggering navigation.</summary>
    public void SelectById(string? id)
    {
        _updating = true;
        try { Selected = Results.FirstOrDefault(r => r.Note.Id == id); }
        finally { _updating = false; }
    }

    [RelayCommand]
    private void Clear() => Query = "";

    public void Dispose() => _debounce.Dispose();
}
