using Zettelkasten.Core.Model;

namespace Zettelkasten.Core.Abstractions;

public interface INoteRepository
{
    /// <summary>Creates and persists an empty note with a fresh unique ID.</summary>
    Task<Note> CreateAsync(CancellationToken ct = default);
    Task<Note?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>Saves everything about the note in one transaction and sets <see cref="Note.UpdatedAt"/>.</summary>
    Task SaveAsync(Note note, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default);
    /// <summary>Summaries for existing IDs, in the order of <paramref name="ids"/>; missing IDs are skipped.</summary>
    Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default);
    /// <summary>Removes the tag from all notes and deletes it.</summary>
    Task DeleteAsync(int tagId, CancellationToken ct = default);
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    bool IsAvailable { get; }
    event EventHandler? AvailabilityChanged;
    /// <summary>Loads the model. Never throws; on failure <see cref="IsAvailable"/> stays false.</summary>
    Task InitializeAsync(CancellationToken ct = default);
    /// <summary>Returns an L2-normalized vector. Only call when <see cref="IsAvailable"/> is true.</summary>
    float[] Embed(string text);
}

public interface ITagSuggestionService
{
    Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default);
}

public interface IBackupService
{
    Task ExportAsync(string destinationPath, CancellationToken ct = default);
}
