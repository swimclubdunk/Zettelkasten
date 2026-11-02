using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Support;

/// <summary>Returns the repository's most recent notes for any query.</summary>
public sealed class FakeSearchService(INoteRepository notes) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default) =>
        (await notes.GetRecentAsync(50, ct)).Select(n => new SearchResult(n, false, false)).ToList();
}
