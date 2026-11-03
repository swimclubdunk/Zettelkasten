using Microsoft.EntityFrameworkCore;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Graph;
using Zettelkasten.Core.Ids;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Core.Tags;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Notes;

public sealed class NoteRepository(
    IDbContextFactory<AppDbContext> factory,
    IEmbeddingService embeddings,
    VectorIndex index,
    TimeProvider time) : INoteRepository
{
    public async Task<Note> CreateAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = time.GetLocalNow();
        var candidate = NoteIdGenerator.Generate(now, _ => false);
        var taken = (await db.Notes
                .Where(n => string.Compare(n.Id, candidate) >= 0)
                .OrderBy(n => n.Id).Select(n => n.Id).Take(10_000)
                .ToListAsync(ct))
            .ToHashSet();
        var id = NoteIdGenerator.Generate(now, taken.Contains);
        var ms = now.ToUnixTimeMilliseconds();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Notes.Add(new NoteEntity { Id = id, CreatedAtMs = ms, UpdatedAtMs = ms });
        await db.SaveChangesAsync(ct);
        await FtsIndex.UpsertAsync(db, id, "", "", [], ct);
        await tx.CommitAsync(ct);

        return new Note { Id = id, CreatedAt = now, UpdatedAt = now };
    }

    public async Task<Note?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.Notes.AsNoTracking()
            .Include(n => n.NoteTags).ThenInclude(nt => nt.Tag)
            .SingleOrDefaultAsync(n => n.Id == id, ct);
        if (e is null) return null;

        var manual = await db.NoteLinks.AsNoTracking()
            .Where(l => l.SourceId == id && l.Kind == LinkKind.Manual)
            .OrderBy(l => l.TargetId).Select(l => l.TargetId)
            .ToListAsync(ct);

        return new Note
        {
            Id = e.Id,
            Content = e.Content,
            CreatedAt = FromMs(e.CreatedAtMs),
            UpdatedAt = FromMs(e.UpdatedAtMs),
            Tags = e.NoteTags.Select(nt => nt.Tag!.Name).Order().ToList(),
            ManualLinks = manual,
        };
    }

    public async Task SaveAsync(Note note, CancellationToken ct = default)
    {
        var tags = note.Tags.Select(TagNormalizer.Normalize).Where(t => t.Length > 0).Distinct().ToList();
        var manual = note.ManualLinks.Where(id => NoteIdGenerator.IsValid(id) && id != note.Id).Distinct().ToList();
        var inline = LinkParser.ExtractTargets(note.Content).Where(id => id != note.Id).ToList();
        var hash = ContentHash.Compute(note.Content);

        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Notes
                         .Include(n => n.NoteTags)
                         .Include(n => n.Embedding)
                         .SingleOrDefaultAsync(n => n.Id == note.Id, ct)
                     ?? throw new InvalidOperationException($"Note {note.Id} does not exist.");

        float[]? vector = null;
        if (embeddings.IsAvailable && entity.Embedding?.ContentHash != hash)
            vector = await Task.Run(() => embeddings.Embed(note.Content), ct);

        var now = time.GetLocalNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        entity.Title = note.Title; // cached first line, for lists and links
        entity.Content = note.Content;
        entity.UpdatedAtMs = now.ToUnixTimeMilliseconds();

        // Tags: reuse existing (case-insensitive), create missing with an embedding when possible
        var tagEntities = await db.Tags.Where(t => tags.Contains(t.Name)).ToListAsync(ct);
        foreach (var name in tags.Where(n => tagEntities.All(t => !string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var created = new TagEntity
            {
                Name = name,
                Embedding = embeddings.IsAvailable ? VectorMath.ToBytes(embeddings.Embed(ContentHash.TagText(name))) : null,
            };
            db.Tags.Add(created);
            tagEntities.Add(created);
        }
        var keepIds = tagEntities.Where(t => t.Id != 0).Select(t => t.Id).ToHashSet();
        entity.NoteTags.RemoveAll(nt => !keepIds.Contains(nt.TagId));
        foreach (var tag in tagEntities.Where(t => t.Id == 0 || entity.NoteTags.All(nt => nt.TagId != t.Id)))
            entity.NoteTags.Add(new NoteTagEntity { NoteId = entity.Id, Tag = tag });

        if (vector is not null)
        {
            entity.Embedding ??= new NoteEmbeddingEntity { NoteId = entity.Id, Vector = [], ContentHash = "" };
            entity.Embedding.Vector = VectorMath.ToBytes(vector);
            entity.Embedding.ContentHash = hash;
        }

        await db.SaveChangesAsync(ct);

        await db.NoteLinks.Where(l => l.SourceId == note.Id).ExecuteDeleteAsync(ct);
        db.NoteLinks.AddRange(inline.Select(t => new NoteLinkEntity { SourceId = note.Id, TargetId = t, Kind = LinkKind.Inline }));
        db.NoteLinks.AddRange(manual.Select(t => new NoteLinkEntity { SourceId = note.Id, TargetId = t, Kind = LinkKind.Manual }));
        await db.SaveChangesAsync(ct);

        await FtsIndex.UpsertAsync(db, note.Id, note.Title, note.Content, tags, ct);
        await tx.CommitAsync(ct);

        if (vector is not null) index.Set(note.Id, vector);
        note.UpdatedAt = now;
        note.Tags = tags.Order().ToList();
        note.ManualLinks = manual;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // ON DELETE CASCADE removes NoteTags, outgoing NoteLinks and the embedding
        await db.Notes.Where(n => n.Id == id).ExecuteDeleteAsync(ct);
        await FtsIndex.DeleteAsync(db, id, ct);
        await tx.CommitAsync(ct);
        index.Remove(id);
    }

    public async Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().OrderByDescending(n => n.UpdatedAtMs).Take(count)
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        return rows.Select(r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs))).ToList();
    }

    public async Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().OrderBy(n => n.Title)
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        return rows.Select(r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs))).ToList();
    }

    public async Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().Where(n => ids.Contains(n.Id))
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id, r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs)));
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (
                from l in db.NoteLinks.AsNoTracking()
                where l.SourceId == id
                join n in db.Notes on l.TargetId equals n.Id into targets
                from n in targets.DefaultIfEmpty()
                orderby l.Kind, l.TargetId
                select new { l.TargetId, Title = n != null ? n.Title : null, l.Kind })
            .ToListAsync(ct);
        return rows.Select(r => new LinkInfo(r.TargetId, r.Title, r.Kind)).ToList();
    }

    public async Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (
                from l in db.NoteLinks.AsNoTracking()
                where l.TargetId == id
                join n in db.Notes on l.SourceId equals n.Id
                orderby n.Title, l.Kind
                select new { l.SourceId, n.Title, l.Kind, Content = l.Kind == LinkKind.Inline ? n.Content : null })
            .ToListAsync(ct);

        // Titles for every note the context snippets may mention, fetched in one query
        var mentioned = rows.Where(r => r.Content is not null)
            .SelectMany(r => LinkParser.ExtractTargets(r.Content!)).Distinct().ToList();
        var titles = await db.Notes.AsNoTracking().Where(n => mentioned.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Title, ct);

        return rows.Select(r => new LinkInfo(r.SourceId, r.Title, r.Kind,
            r.Content is null ? null : LinkContext.Extract(r.Content, id, titles.GetValueOrDefault))).ToList();
    }

    public async Task<LinkGraph> GetNeighborhoodAsync(string id, int depth = 2, int maxNodes = 40, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await LinkNeighborhood.BuildAsync(id, depth, maxNodes,
            async ids => await db.NoteLinks.AsNoTracking()
                .Where(l => ids.Contains(l.SourceId) || ids.Contains(l.TargetId))
                .Select(l => new GraphEdge(l.SourceId, l.TargetId, l.Kind))
                .ToListAsync(ct),
            async ids => await db.Notes.AsNoTracking()
                .Where(n => ids.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, n => n.Title, ct));
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Notes.CountAsync(ct);
    }

    private static DateTimeOffset FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
}
