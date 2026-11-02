namespace Zettelkasten.Core.Model;

public sealed class Note
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Normalized tag names.</summary>
    public List<string> Tags { get; set; } = [];
    /// <summary>Target note IDs of manual ("related") links.</summary>
    public List<string> ManualLinks { get; set; } = [];

    public Note Clone() => new()
    {
        Id = Id, Title = Title, Content = Content, CreatedAt = CreatedAt, UpdatedAt = UpdatedAt,
        Tags = [.. Tags], ManualLinks = [.. ManualLinks],
    };
}

public sealed record NoteSummary(string Id, string Title, DateTimeOffset UpdatedAt)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title;
}

public enum LinkKind { Inline = 0, Manual = 1 }

/// <summary>A link as shown in the side panel. <see cref="Title"/> is null when the target note doesn't exist.</summary>
public sealed record LinkInfo(string NoteId, string? Title, LinkKind Kind)
{
    public bool IsBroken => Title is null;
    public string DisplayTitle => Title is null ? NoteId : string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title;
}

public sealed record Tag(int Id, string Name);

public sealed record TagWithEmbedding(Tag Tag, float[]? Embedding);

public sealed record SearchResult(NoteSummary Note, bool MatchedKeyword, bool MatchedSemantic);

public sealed record TagSuggestion(Tag Tag, float Score, bool IsInitialsMatch);

public sealed record TagSuggestions(Tag? ExactMatch, IReadOnlyList<TagSuggestion> Similar);
