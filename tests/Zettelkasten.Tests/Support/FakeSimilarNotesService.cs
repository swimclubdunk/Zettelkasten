using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Support;

public sealed class FakeSimilarNotesService : ISimilarNotesService
{
    public List<SimilarNote> Result { get; } = [];
    public List<string> Requests { get; } = [];

    public Task<IReadOnlyList<SimilarNote>> FindAsync(string noteId, int limit = 5, CancellationToken ct = default)
    {
        Requests.Add(noteId);
        return Task.FromResult<IReadOnlyList<SimilarNote>>(Result.Take(limit).ToList());
    }
}
