using System.Globalization;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Support;

/// <summary>In-memory INoteRepository for view model tests.</summary>
public sealed class FakeNoteRepository(TimeProvider? time = null) : INoteRepository
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, Note> _notes = [];
    private long _counter;

    public int SaveCount { get; private set; }
    public List<Note> Saved { get; } = [];
    public bool FailNextSave { get; set; }

    public Note Add(string title, string content = "", params string[] tags)
    {
        var id = (20261102000000 + ++_counter).ToString(CultureInfo.InvariantCulture);
        var at = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero).AddSeconds(_counter);
        var note = new Note { Id = id, Title = title, Content = content, Tags = [.. tags], CreatedAt = at, UpdatedAt = at };
        _notes[id] = note;
        return note.Clone();
    }

    public Task<Note> CreateAsync(CancellationToken ct = default) => Task.FromResult(Add(""));

    public Task<Note?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_notes.TryGetValue(id, out var n) ? n.Clone() : null);

    public Task SaveAsync(Note note, CancellationToken ct = default)
    {
        if (FailNextSave) { FailNextSave = false; throw new IOException("disk full"); }
        if (!_notes.ContainsKey(note.Id)) throw new InvalidOperationException("missing");
        SaveCount++;
        note.UpdatedAt = _time.GetLocalNow();
        _notes[note.Id] = note.Clone();
        Saved.Add(note.Clone());
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) { _notes.Remove(id); return Task.CompletedTask; }

    public Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default) =>
        Result(_notes.Values.OrderByDescending(n => n.UpdatedAt).ThenByDescending(n => n.Id).Take(count).Select(Summary));

    public Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default) =>
        Result(_notes.Values.OrderBy(n => n.Title).Select(Summary));

    public Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default) =>
        Result(ids.Where(_notes.ContainsKey).Select(id => Summary(_notes[id])));

    public Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default)
    {
        if (!_notes.TryGetValue(id, out var n)) return Result<LinkInfo>([]);
        var inline = LinkParser.ExtractTargets(n.Content).Where(t => t != id)
            .Select(t => new LinkInfo(t, _notes.GetValueOrDefault(t)?.Title, LinkKind.Inline));
        var manual = n.ManualLinks.Distinct()
            .Select(t => new LinkInfo(t, _notes.GetValueOrDefault(t)?.Title, LinkKind.Manual));
        return Result(inline.Concat(manual));
    }

    public Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default)
    {
        var inline = _notes.Values.Where(n => n.Id != id && LinkParser.ExtractTargets(n.Content).Contains(id))
            .Select(n => new LinkInfo(n.Id, n.Title, LinkKind.Inline));
        var manual = _notes.Values.Where(n => n.ManualLinks.Contains(id))
            .Select(n => new LinkInfo(n.Id, n.Title, LinkKind.Manual));
        return Result(inline.Concat(manual));
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_notes.Count);

    private static NoteSummary Summary(Note n) => new(n.Id, n.Title, n.UpdatedAt);
    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToList());
}
