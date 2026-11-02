using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.ViewModels;

public sealed partial class NoteEditorViewModel : ObservableObject, IDisposable
{
    public const string SaveFailedMessage = "Save failed – your text is still in the editor";

    private readonly INoteRepository _repo;
    private readonly ILogger<NoteEditorViewModel> _logger;
    private readonly Debouncer _autoSave;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Note? _note;
    private int _version;
    private int _savedVersion;
    private bool _loading;

    public NoteEditorViewModel(INoteRepository repo, TimeProvider time, ILogger<NoteEditorViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
        _autoSave = new Debouncer(TimeSpan.FromSeconds(1), time);
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _isPreview;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private DateTimeOffset? _lastSavedAt;

    public Note? CurrentNote => _note;
    public string? NoteId => _note?.Id;
    public bool HasNote => _note is not null;
    public bool IsDirty => _version != _savedVersion;

    /// <summary>The most recently scheduled auto-save (exposed for tests).</summary>
    internal Task PendingSave { get; private set; } = Task.CompletedTask;

    public event EventHandler? NoteLoaded;
    public event EventHandler<Note>? Saved;
    public event EventHandler<string>? OpenRequested;

    /// <summary>Saves pending edits of the current note first, then shows <paramref name="note"/>.</summary>
    /// <returns>
    /// False when the pending edits could not be saved: the current note then stays loaded so nothing is lost,
    /// unless <paramref name="discardUnsaved"/> says the user agreed to drop them.
    /// </returns>
    public async Task<bool> LoadAsync(Note? note, bool discardUnsaved = false)
    {
        await FlushAsync();
        if (IsDirty && !discardUnsaved) return false;
        _loading = true;
        try
        {
            _note = note;
            Title = note?.Title ?? "";
            Content = note?.Content ?? "";
        }
        finally
        {
            _loading = false;
        }
        _version = _savedVersion = 0;
        SaveError = null;
        OnPropertyChanged(nameof(CurrentNote));
        OnPropertyChanged(nameof(NoteId));
        OnPropertyChanged(nameof(HasNote));
        NoteLoaded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task FlushAsync()
    {
        _autoSave.Cancel();
        await SaveCoreAsync();
    }

    [RelayCommand]
    private Task Save() => FlushAsync();

    /// <summary>Applies a change to tags/links and saves right away.</summary>
    public async Task UpdateAndSaveAsync(Action<Note> mutate)
    {
        if (_note is null) return;
        mutate(_note);
        _version++;
        await FlushAsync();
    }

    public void RequestOpen(string noteId) => OpenRequested?.Invoke(this, noteId);

    public async Task<IReadOnlyList<NoteSummary>> GetLinkCandidatesAsync() =>
        (await _repo.GetAllSummariesAsync()).Where(s => s.Id != NoteId).ToList();

    public async Task<IReadOnlyDictionary<string, string>> GetLinkTitlesAsync()
    {
        var summaries = await _repo.GetSummariesAsync(LinkParser.ExtractTargets(Content));
        return summaries.ToDictionary(s => s.Id, s => s.Title);
    }

    partial void OnTitleChanged(string value) => MarkDirty();
    partial void OnContentChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || _note is null) return;
        _version++;
        PendingSave = _autoSave.Trigger(SaveCoreAsync);
    }

    private async Task SaveCoreAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var note = _note;
            if (note is null || !IsDirty) return;

            var version = _version;
            note.Title = Title;
            note.Content = Content;
            var snapshot = note.Clone();
            try
            {
                await _repo.SaveAsync(snapshot);
                note.UpdatedAt = snapshot.UpdatedAt;
                if (_version == version)
                {
                    // Take the repository's normalized lists, unless a tag/link change arrived during the save
                    note.Tags = snapshot.Tags;
                    note.ManualLinks = snapshot.ManualLinks;
                }
                _savedVersion = version;
                SaveError = null;
                LastSavedAt = snapshot.UpdatedAt;
                Saved?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving note {NoteId} failed", note.Id);
                SaveError = SaveFailedMessage;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose()
    {
        _autoSave.Dispose();
        _saveLock.Dispose();
    }
}
