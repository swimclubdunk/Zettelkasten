using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.App;

public sealed class SearchViewModelTests : IDisposable
{
    private sealed class StubSearch : ISearchService
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, TaskCompletionSource<IReadOnlyList<SearchResult>>> Pending { get; } = [];
        public bool Manual { get; set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            Calls.Add(query);
            if (!Manual) return Task.FromResult<IReadOnlyList<SearchResult>>([Result(query)]);
            var tcs = new TaskCompletionSource<IReadOnlyList<SearchResult>>();
            Pending[query] = tcs;
            return tcs.Task;
        }
    }

    private static SearchResult Result(string id) =>
        new(new NoteSummary(id, $"title {id}", DateTimeOffset.UnixEpoch), true, false);

    private readonly FakeTimeProvider _time = new();
    private readonly StubSearch _search = new();
    private readonly SearchViewModel _vm;

    public SearchViewModelTests() =>
        _vm = new SearchViewModel(_search, _time, NullLogger<SearchViewModel>.Instance);

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task Query_changes_are_debounced_250ms()
    {
        _vm.Query = "m";
        _vm.Query = "mem";
        _time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Empty(_search.Calls);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await _vm.PendingSearch;

        Assert.Equal(new[] { "mem" }, _search.Calls);
        Assert.Equal("mem", Assert.Single(_vm.Results).Note.Id);
    }

    [Fact]
    public async Task Stale_results_are_discarded()
    {
        _search.Manual = true;
        _vm.Query = "a";
        var first = _vm.RefreshAsync();
        _vm.Query = "ab";
        var second = _vm.RefreshAsync();

        _search.Pending["ab"].SetResult([Result("x")]);
        await second;
        _search.Pending["a"].SetResult([Result("y")]);
        await first;

        Assert.Equal("x", Assert.Single(_vm.Results).Note.Id);
    }

    [Fact]
    public async Task User_selection_requests_open_but_refresh_and_SelectById_do_not()
    {
        var opened = new List<string>();
        _vm.OpenRequested += (_, id) => opened.Add(id);
        await _vm.RefreshAsync();             // query "" -> result id ""
        _vm.SelectById("");
        await _vm.RefreshAsync();             // selection restored silently
        Assert.Empty(opened);

        _vm.Selected = null;
        _vm.Selected = _vm.Results[0];
        Assert.Equal(new[] { "" }, opened);
    }
}
