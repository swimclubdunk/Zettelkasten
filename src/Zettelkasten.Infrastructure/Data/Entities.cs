using Zettelkasten.Core.Model;

namespace Zettelkasten.Infrastructure.Data;

public sealed class NoteEntity
{
    public required string Id { get; set; }
    /// <summary>Cached first line of <see cref="Content"/> (notes have no own title); set on every save.</summary>
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public long CreatedAtMs { get; set; }
    public long UpdatedAtMs { get; set; }
    public List<NoteTagEntity> NoteTags { get; set; } = [];
    public NoteEmbeddingEntity? Embedding { get; set; }
}

public sealed class TagEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public byte[]? Embedding { get; set; }
    public List<NoteTagEntity> NoteTags { get; set; } = [];
}

public sealed class NoteTagEntity
{
    public required string NoteId { get; set; }
    public int TagId { get; set; }
    public NoteEntity? Note { get; set; }
    public TagEntity? Tag { get; set; }
}

public sealed class NoteLinkEntity
{
    public required string SourceId { get; set; }
    /// <summary>Not a foreign key: links may point to notes that don't exist (broken links).</summary>
    public required string TargetId { get; set; }
    public LinkKind Kind { get; set; }
}

public sealed class NoteEmbeddingEntity
{
    public required string NoteId { get; set; }
    public required byte[] Vector { get; set; }
    public required string ContentHash { get; set; }
}
