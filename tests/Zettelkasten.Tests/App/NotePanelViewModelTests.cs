using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class NotePanelViewModelTests : IDisposable
{
    private sealed class StubSuggestions : ITagSuggestionService
    {
        public TagSuggestions Result { get; set; } = new(null, []);
        public Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default) => Task.FromResult(Result);
    }

    private sealed class StubTags : ITagRepository
    {
        public List<Tag> All { get; } = [];
        public List<int> Deleted { get; } = [];
        public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Tag>>(All);
        public Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TagWithEmbedding>>(All.Select(t => new TagWithEmbedding(t, null)).ToList());
        public Task DeleteAsync(int tagId, CancellationToken ct = default) { Deleted.Add(tagId); return Task.CompletedTask; }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly StubSuggestions _suggest = new();
    private readonly StubTags _tags = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly NoteEditorViewModel _editor;
    private readonly NotePanelViewModel _vm;

    public NotePanelViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        _editor = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
        _vm = new NotePanelViewModel(_repo, _tags, _suggest, _editor, _dialogs, _time);
    }

    public void Dispose() { _vm.Dispose(); _editor.Dispose(); }

    private async Task<Note> OpenAsync(Note note)
    {
        await _editor.LoadAsync(note);
        await _vm.RefreshAsync();
        return note;
    }

    [Fact]
    public async Task Refresh_splits_related_outgoing_and_backlinks()
    {
        var target = _repo.Add("Target");
        var related = _repo.Add("Related");
        var note = _repo.Add("Note", $"[[{target.Id}]] [[20990101000000]]", "memory");
        await _editor.LoadAsync(note);
        await _editor.UpdateAndSaveAsync(n => n.ManualLinks.Add(related.Id));
        _repo.Add("Fan", $"[[{note.Id}]]");

        await _vm.RefreshAsync();

        Assert.Equal(new[] { "memory" }, _vm.Tags);
        Assert.Equal(related.Id, Assert.Single(_vm.Related).NoteId);
        Assert.Equal(2, _vm.Outgoing.Count);
        Assert.Contains(_vm.Outgoing, l => l.IsBroken);
        Assert.Equal("Fan", Assert.Single(_vm.Backlinks).Title);
    }

    [Fact]
    public async Task Enter_with_similar_tags_shows_suggestions_instead_of_creating()
    {
        await OpenAsync(_repo.Add("n"));
        var existing = new Tag(1, "machine-learning");
        _suggest.Result = new TagSuggestions(null, [new TagSuggestion(existing, 0.9f, false)]);
        _vm.TagInput = "deep learning";

        await _vm.AddTagFromInputCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Tags);
        Assert.Single(_vm.Suggestions);
        Assert.True(_vm.CanCreateTag);

        await _vm.AcceptSuggestionCommand.ExecuteAsync(_vm.Suggestions[0]);
        Assert.Equal(new[] { "machine-learning" }, _vm.Tags);
        Assert.Equal("", _vm.TagInput);
        Assert.Empty(_vm.Suggestions);
    }

    [Fact]
    public async Task Enter_with_exact_match_reuses_existing_tag()
    {
        await OpenAsync(_repo.Add("n"));
        _suggest.Result = new TagSuggestions(new Tag(1, "machine-learning"), []);
        _vm.TagInput = "Machine Learning";
        await _vm.AddTagFromInputCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "machine-learning" }, _vm.Tags);
    }

    [Fact]
    public async Task Create_anyway_adds_normalized_input()
    {
        await OpenAsync(_repo.Add("n"));
        _vm.TagInput = "Neural Nets";
        await _vm.CreateTagAnywayCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "neural-nets" }, _vm.Tags);
        Assert.Equal("neural-nets", Assert.Single(_repo.Saved[^1].Tags));
    }

    [Fact]
    public async Task Typing_updates_suggestions_after_debounce()
    {
        await OpenAsync(_repo.Add("n"));
        _suggest.Result = new TagSuggestions(null, [new TagSuggestion(new Tag(1, "memory"), 0.8f, false)]);
        _vm.TagInput = "memroy";
        Assert.Empty(_vm.Suggestions);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        await _vm.PendingSuggest;
        Assert.Single(_vm.Suggestions);
    }

    [Fact]
    public async Task Manual_link_uses_picker_and_can_be_removed()
    {
        var other = _repo.Add("Other");
        await OpenAsync(_repo.Add("n"));
        _dialogs.PickNoteResult = other.Id;

        await _vm.AddManualLinkCommand.ExecuteAsync(null);
        var link = Assert.Single(_vm.Related);
        Assert.Equal(other.Id, link.NoteId);

        await _vm.RemoveManualLinkCommand.ExecuteAsync(link);
        Assert.Empty(_vm.Related);
    }

    [Fact]
    public async Task Delete_tag_everywhere_requires_confirmation()
    {
        _tags.All.Add(new Tag(7, "memory"));
        await OpenAsync(_repo.Add("n", "", "memory"));

        _dialogs.ConfirmResult = false;
        await _vm.DeleteTagEverywhereCommand.ExecuteAsync("memory");
        Assert.Empty(_tags.Deleted);

        _dialogs.ConfirmResult = true;
        await _vm.DeleteTagEverywhereCommand.ExecuteAsync("memory");
        Assert.Equal(new[] { 7 }, _tags.Deleted);
        Assert.Empty(_vm.Tags);
    }

    [Fact]
    public void Open_link_ignores_broken_links()
    {
        var opened = new List<string>();
        _vm.OpenRequested += (_, id) => opened.Add(id);
        _vm.OpenLinkCommand.Execute(new LinkInfo("20990101000000", null, LinkKind.Inline));
        _vm.OpenLinkCommand.Execute(new LinkInfo("20261102000001", "x", LinkKind.Inline));
        Assert.Equal(new[] { "20261102000001" }, opened);
    }
}
