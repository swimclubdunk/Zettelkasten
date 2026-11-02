using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class MainViewModelTests
{
    private sealed class StubBackup : IBackupService
    {
        public List<string> Exports { get; } = [];
        public Task ExportAsync(string destinationPath, CancellationToken ct = default)
        {
            Exports.Add(destinationPath);
            return Task.CompletedTask;
        }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly FakeDialogService _dialogs = new();
    private readonly StubBackup _backup = new();
    private readonly FakeEmbeddingService _embeddings = new() { IsAvailable = false };
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        var editor = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
        var search = new SearchViewModel(new FakeSearchService(_repo), _time, NullLogger<SearchViewModel>.Instance);
        var panel = new NotePanelViewModel(_repo, new TagRepositoryStub(), new SuggestionStub(), editor, _dialogs, _time);
        _vm = new MainViewModel(_repo, _embeddings, _backup, _dialogs, search, editor, panel, _time,
            NullLogger<MainViewModel>.Instance);
    }

    private sealed class TagRepositoryStub : ITagRepository
    {
        public Task<IReadOnlyList<Zettelkasten.Core.Model.Tag>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Zettelkasten.Core.Model.Tag>>([]);
        public Task<IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding>>([]);
        public Task DeleteAsync(int tagId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SuggestionStub : ITagSuggestionService
    {
        public Task<Zettelkasten.Core.Model.TagSuggestions> SuggestAsync(string input, CancellationToken ct = default) =>
            Task.FromResult(new Zettelkasten.Core.Model.TagSuggestions(null, []));
    }

    [Fact]
    public async Task Initialize_opens_most_recent_note_and_counts()
    {
        _repo.Add("old");
        var recent = _repo.Add("recent");
        await _vm.InitializeAsync();
        Assert.Equal(recent.Id, _vm.Editor.NoteId);
        Assert.Equal(2, _vm.NoteCount);
        Assert.Equal(recent.Id, _vm.Search.Selected?.Note.Id);
    }

    [Fact]
    public async Task New_note_is_created_opened_and_back_returns()
    {
        var first = _repo.Add("first");
        await _vm.InitializeAsync();
        var focused = false;
        _vm.FocusEditorRequested += (_, _) => focused = true;

        await _vm.NewNoteCommand.ExecuteAsync(null);

        Assert.NotEqual(first.Id, _vm.Editor.NoteId);
        Assert.True(focused);
        Assert.Equal(2, _vm.NoteCount);
        Assert.True(_vm.BackCommand.CanExecute(null));

        await _vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(first.Id, _vm.Editor.NoteId);
        Assert.True(_vm.ForwardCommand.CanExecute(null));
    }

    [Fact]
    public async Task Opening_missing_note_keeps_current_and_reports()
    {
        var a = _repo.Add("a");
        await _vm.InitializeAsync();
        await _vm.OpenAsync("20990101000000");
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        Assert.Contains("20990101000000", _vm.StatusMessage);
    }

    [Fact]
    public async Task Panel_and_editor_link_requests_navigate()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.InitializeAsync();          // opens b
        _vm.Editor.RequestOpen(a.Id);
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        _vm.Panel.OpenLinkCommand.Execute(new Zettelkasten.Core.Model.LinkInfo(b.Id, "b", Zettelkasten.Core.Model.LinkKind.Inline));
        Assert.Equal(b.Id, _vm.Editor.NoteId);
    }

    [Fact]
    public async Task Delete_lists_backlinks_and_falls_back_to_previous_note()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        _repo.Add("linker", $"[[{b.Id}]]");
        await _vm.InitializeAsync();          // opens linker (most recent)
        await _vm.OpenAsync(a.Id);
        await _vm.OpenAsync(b.Id);

        await _vm.DeleteNoteCommand.ExecuteAsync(null);

        Assert.Contains("linker", Assert.Single(_dialogs.Confirmations));
        Assert.Null(await _repo.GetAsync(b.Id));
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        Assert.Equal(2, _vm.NoteCount);
    }

    [Fact]
    public async Task Navigating_away_after_failed_save_asks_and_stays_when_declined()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.InitializeAsync();          // opens b
        _vm.Editor.Content = "unsaved";
        _repo.FailNextSave = true;
        _dialogs.ConfirmResult = false;

        await _vm.OpenAsync(a.Id);

        Assert.Equal(b.Id, _vm.Editor.NoteId);
        Assert.Equal("unsaved", _vm.Editor.Content);
        Assert.Single(_dialogs.Confirmations);

        _repo.FailNextSave = true;
        _dialogs.ConfirmResult = true;
        await _vm.OpenAsync(a.Id);
        Assert.Equal(a.Id, _vm.Editor.NoteId);
    }

    [Fact]
    public async Task New_note_after_failed_save_creates_nothing_when_declined()
    {
        _repo.Add("a");
        await _vm.InitializeAsync();
        _vm.Editor.Content = "unsaved";
        _repo.FailNextSave = true;
        _dialogs.ConfirmResult = false;

        await _vm.NewNoteCommand.ExecuteAsync(null);

        Assert.Equal(1, await _repo.CountAsync());
        Assert.Equal("unsaved", _vm.Editor.Content);
    }

    [Fact]
    public async Task Delete_cancelled_keeps_note()
    {
        var a = _repo.Add("a");
        await _vm.InitializeAsync();
        _dialogs.ConfirmResult = false;
        await _vm.DeleteNoteCommand.ExecuteAsync(null);
        Assert.NotNull(await _repo.GetAsync(a.Id));
        Assert.Equal(a.Id, _vm.Editor.NoteId);
    }

    [Fact]
    public async Task Export_backup_uses_chosen_path_and_cancel_does_nothing()
    {
        await _vm.ExportBackupCommand.ExecuteAsync(null);
        Assert.Empty(_backup.Exports);

        _dialogs.SaveFileResult = @"C:\backups\zk.db";
        await _vm.ExportBackupCommand.ExecuteAsync(null);
        Assert.Equal(new[] { @"C:\backups\zk.db" }, _backup.Exports);
        Assert.Contains(@"C:\backups\zk.db", _vm.StatusMessage);
    }

    [Fact]
    public void Model_status_reflects_availability()
    {
        Assert.Contains("loading", _vm.ModelStatus);
        _vm.OnModelLoadCompleted();
        Assert.Contains("unavailable", _vm.ModelStatus);
        _embeddings.IsAvailable = true;
        Assert.Contains("ready", _vm.ModelStatus);
    }
}
