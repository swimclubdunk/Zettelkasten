using Microsoft.EntityFrameworkCore;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Tags;

public sealed class TagRepository(IDbContextFactory<AppDbContext> factory) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new Tag(t.Id, t.Name)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return rows.Select(t => new TagWithEmbedding(
            new Tag(t.Id, t.Name),
            t.Embedding is null ? null : VectorMath.FromBytes(t.Embedding))).ToList();
    }

    public async Task DeleteAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var affected = await db.NoteTags.Where(nt => nt.TagId == tagId).Select(nt => nt.NoteId).ToListAsync(ct);
        await db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync(ct); // cascades NoteTags

        // Tag names are part of the FTS row, so refresh it for every affected note
        var notes = await db.Notes.AsNoTracking()
            .Where(n => affected.Contains(n.Id))
            .Select(n => new { n.Id, n.Title, n.Content, Tags = n.NoteTags.Select(nt => nt.Tag!.Name).ToList() })
            .ToListAsync(ct);
        foreach (var n in notes)
            await FtsIndex.UpsertAsync(db, n.Id, n.Title, n.Content, n.Tags, ct);

        await tx.CommitAsync(ct);
    }
}
