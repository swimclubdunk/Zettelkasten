using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class NoteEditorViewModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly NoteEditorViewModel _vm;

    public NoteEditorViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        _vm = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
    }

    public void Dispose() => _vm.Dispose();

    /// <summary>Holds the next save until released, so a save can be observed "in flight".</summary>
    private sealed class GatedRepository(FakeNoteRepository inner) : INoteRepository
    {
        private TaskCompletionSource? _gate;
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HoldNextSave() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _gate?.SetResult();

        public async Task SaveAsync(Note note, CancellationToken ct = default)
        {
            if (_gate is { } gate)
            {
                SaveStarted.TrySetResult();
                await gate.Task;
                _gate = null;
            }
            await inner.SaveAsync(note, ct);
        }

        public Task<Note> CreateAsync(CancellationToken ct = default) => inner.CreateAsync(ct);
        public Task<Note?> GetAsync(string id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default) => inner.GetRecentAsync(count, ct);
        public Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default) => inner.GetAllSummariesAsync(ct);
        public Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => inner.GetSummariesAsync(ids, ct);
        public Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default) => inner.GetOutgoingLinksAsync(id, ct);
        public Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default) => inner.GetBacklinksAsync(id, ct);
        public Task<Zettelkasten.Core.Graph.LinkGraph> GetNeighborhoodAsync(string id, int depth = 2, int maxNodes = 40, CancellationToken ct = default) =>
            inner.GetNeighborhoodAsync(id, depth, maxNodes, ct);
        public Task<int> CountAsync(CancellationToken ct = default) => inner.CountAsync(ct);
    }

    [Fact]
    public async Task Switching_note_after_failed_save_is_refused_and_keeps_edits()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.LoadAsync(a);
        _vm.Content = "important";
        _repo.FailNextSave = true;

        var switched = await _vm.LoadAsync(b);

        Assert.False(switched);
        Assert.Equal(a.Id, _vm.NoteId);
        Assert.Equal("important", _vm.Content);
        Assert.Equal(NoteEditorViewModel.SaveFailedMessage, _vm.SaveError);

        Assert.True(await _vm.LoadAsync(b, discardUnsaved: true));
        Assert.Equal(b.Id, _vm.NoteId);
    }

    [Fact]
    public async Task Tag_change_during_in_flight_autosave_is_kept()
    {
        var gated = new GatedRepository(_repo);
        using var vm = new NoteEditorViewModel(gated, _time, NullLogger<NoteEditorViewModel>.Instance);
        var note = _repo.Add("t");
        await vm.LoadAsync(note);

        gated.HoldNextSave();
        vm.Content = "edited";
        _time.Advance(TimeSpan.FromSeconds(1));
        var autoSave = vm.PendingSave;
        await gated.SaveStarted.Task;                       // auto-save holds the snapshot without "x"

        var update = vm.UpdateAndSaveAsync(n => n.Tags.Add("x"));
        gated.Release();
        await autoSave;
        await update;

        Assert.Contains("x", vm.CurrentNote!.Tags);
        Assert.Contains("x", (await _repo.GetAsync(note.Id))!.Tags);
        Assert.Equal("edited", (await _repo.GetAsync(note.Id))!.Content);
    }

    [Fact]
    public async Task Edits_are_saved_after_one_second_of_inactivity()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        _vm.Content = "a";
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _vm.Content = "ab";
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, _repo.SaveCount);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await _vm.PendingSave;

        Assert.Equal(1, _repo.SaveCount);
        Assert.Equal("ab", _repo.Saved[^1].Content);
        Assert.False(_vm.IsDirty);
        Assert.NotNull(_vm.LastSavedAt);
    }

    [Fact]
    public async Task Loading_a_note_does_not_save()
    {
        await _vm.LoadAsync(_repo.Add("t", "body"));
        _time.Advance(TimeSpan.FromSeconds(5));
        await _vm.PendingSave;
        Assert.Equal(0, _repo.SaveCount);
        Assert.Equal("# t\nbody", _vm.Content);
    }

    [Fact]
    public async Task Loading_another_note_flushes_pending_edits_to_previous_note()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.LoadAsync(a);
        _vm.Content = "edited";
        _time.Advance(TimeSpan.FromMilliseconds(300));

        await _vm.LoadAsync(b);
        _time.Advance(TimeSpan.FromSeconds(2));
        await _vm.PendingSave;

        var saved = Assert.Single(_repo.Saved);
        Assert.Equal(a.Id, saved.Id);
        Assert.Equal("edited", saved.Content);
        Assert.Equal("b", (await _repo.GetAsync(b.Id))!.Title);
        Assert.Equal("# b", _vm.Content);
        Assert.Equal(b.Id, _vm.NoteId);
    }

    [Fact]
    public async Task Failed_save_keeps_text_reports_error_and_retries()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        _repo.FailNextSave = true;
        _vm.Content = "important";
        _time.Advance(TimeSpan.FromSeconds(1));
        await _vm.PendingSave;

        Assert.Equal(NoteEditorViewModel.SaveFailedMessage, _vm.SaveError);
        Assert.True(_vm.IsDirty);
        Assert.Equal("important", _vm.Content);

        await _vm.SaveCommand.ExecuteAsync(null);   // Ctrl+S
        Assert.Null(_vm.SaveError);
        Assert.Equal("important", _repo.Saved[^1].Content);
    }

    [Fact]
    public async Task UpdateAndSave_saves_immediately()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        await _vm.UpdateAndSaveAsync(n => n.Tags.Add("x"));
        Assert.Equal(1, _repo.SaveCount);
        Assert.Contains("x", _repo.Saved[^1].Tags);
    }

    [Fact]
    public async Task Flush_without_changes_does_nothing()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        await _vm.FlushAsync();
        Assert.Equal(0, _repo.SaveCount);
    }

    [Fact]
    public async Task Link_titles_only_include_existing_notes()
    {
        var target = _repo.Add("Target");
        await _vm.LoadAsync(_repo.Add("t", $"[[{target.Id}]] [[20990101000000]]"));
        var titles = await _vm.GetLinkTitlesAsync();
        Assert.Equal("Target", Assert.Single(titles).Value);
    }

    [Fact]
    public async Task Link_candidates_exclude_current_note()
    {
        var other = _repo.Add("Other");
        var self = _repo.Add("Self");
        await _vm.LoadAsync(self);
        Assert.Equal(new[] { other.Id }, (await _vm.GetLinkCandidatesAsync()).Select(s => s.Id));
    }
}
