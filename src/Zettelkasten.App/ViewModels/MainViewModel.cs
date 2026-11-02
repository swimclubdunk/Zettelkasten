using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.App.Services;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;

namespace Zettelkasten.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly INoteRepository _notes;
    private readonly IEmbeddingService _embeddings;
    private readonly IBackupService _backup;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly ILogger<MainViewModel> _logger;
    private readonly NavigationHistory _history = new();
    private bool _modelLoadCompleted;

    public MainViewModel(INoteRepository notes, IEmbeddingService embeddings, IBackupService backup,
        IDialogService dialogs, SearchViewModel search, NoteEditorViewModel editor, NotePanelViewModel panel,
        TimeProvider time, ILogger<MainViewModel> logger)
    {
        _notes = notes;
        _embeddings = embeddings;
        _backup = backup;
        _dialogs = dialogs;
        _time = time;
        _logger = logger;
        Search = search;
        Editor = editor;
        Panel = panel;

        Search.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Panel.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Editor.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Editor.Saved += (_, _) => Run(OnSavedAsync);
        _embeddings.AvailabilityChanged += (_, _) => UpdateModelStatus();
        UpdateModelStatus();
    }

    public SearchViewModel Search { get; }
    public NoteEditorViewModel Editor { get; }
    public NotePanelViewModel Panel { get; }

    [ObservableProperty] private int _noteCount;
    [ObservableProperty] private string _modelStatus = "";
    [ObservableProperty] private string? _statusMessage;

    public event EventHandler? FocusTitleRequested;

    public async Task InitializeAsync()
    {
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();
        if (Search.Results.FirstOrDefault() is { } first)
            await OpenAsync(first.Note.Id);
    }

    public Task OpenAsync(string id) => OpenCoreAsync(id, recordHistory: true);

    public Task ShutdownAsync() => Editor.FlushAsync();

    public void OnModelLoadCompleted()
    {
        _modelLoadCompleted = true;
        UpdateModelStatus();
    }

    [RelayCommand]
    private async Task NewNote()
    {
        if (!await ReleaseCurrentNoteAsync()) return;
        var note = await _notes.CreateAsync();
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();
        await OpenAsync(note.Id);
        FocusTitleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task Back()
    {
        if (_history.GoBack() is { } id && !await OpenCoreAsync(id, recordHistory: false))
            _history.GoForward(); // stayed on the current note, so undo the history move
        NotifyNavigation();
    }

    private bool CanGoBack() => _history.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task Forward()
    {
        if (_history.GoForward() is { } id && !await OpenCoreAsync(id, recordHistory: false))
            _history.GoBack();
        NotifyNavigation();
    }

    private bool CanGoForward() => _history.CanGoForward;

    [RelayCommand]
    private async Task DeleteNote()
    {
        var note = Editor.CurrentNote;
        if (note is null) return;

        var backlinks = await _notes.GetBacklinksAsync(note.Id);
        var title = string.IsNullOrWhiteSpace(Editor.Title) ? "(untitled)" : Editor.Title;
        var message = $"Delete \"{title}\" ({note.Id})?";
        if (backlinks.Count > 0)
            message += "\n\nThese notes link to it and will show a broken link:\n" +
                       string.Join("\n", backlinks.Select(b => $"• {b.DisplayTitle}").Distinct());
        if (!_dialogs.Confirm("Delete note", message)) return;

        await Editor.LoadAsync(null, discardUnsaved: true); // the note is being deleted anyway
        await _notes.DeleteAsync(note.Id);
        var previous = _history.Remove(note.Id);
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();

        if (previous is not null)
            await OpenCoreAsync(previous, recordHistory: false);
        else if (Search.Results.FirstOrDefault() is { } first)
            await OpenCoreAsync(first.Note.Id, recordHistory: true);
        else
            await Panel.RefreshAsync();
        NotifyNavigation();
    }

    [RelayCommand]
    private async Task ExportBackup()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var path = _dialogs.PickSaveFile("Export backup", $"zettelkasten-backup-{stamp}.db", "SQLite database (*.db)|*.db");
        if (path is null) return;
        try
        {
            await Editor.FlushAsync();
            await _backup.ExportAsync(path);
            StatusMessage = $"Backup saved to {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup export to {Path} failed", path);
            _dialogs.ShowError("Backup failed", ex.Message);
        }
    }

    [RelayCommand]
    private void TogglePreview() => Editor.IsPreview = !Editor.IsPreview;

    /// <returns>False when the note was not opened (missing, or the user kept unsaved edits).</returns>
    private async Task<bool> OpenCoreAsync(string id, bool recordHistory)
    {
        if (Editor.NoteId == id) return true;
        var note = await _notes.GetAsync(id);
        if (note is null)
        {
            StatusMessage = $"Note {id} does not exist.";
            return false;
        }
        if (!await ReleaseCurrentNoteAsync() || !await Editor.LoadAsync(note)) return false;
        if (recordHistory) _history.Visit(id);
        Search.SelectById(id);
        StatusMessage = null;
        await Panel.RefreshAsync();
        NotifyNavigation();
        return true;
    }

    /// <summary>
    /// Saves the open note's pending edits. If that fails, asks whether to discard them;
    /// returns false when the user wants to keep them (the caller must then stay on the note).
    /// </summary>
    private async Task<bool> ReleaseCurrentNoteAsync()
    {
        await Editor.FlushAsync();
        if (!Editor.IsDirty) return true;
        if (!_dialogs.Confirm("Unsaved changes",
                "Your latest changes to this note could not be saved.\n\nDiscard them and continue?"))
            return false;
        await Editor.LoadAsync(null, discardUnsaved: true);
        return true;
    }

    private async Task OnSavedAsync()
    {
        await Panel.RefreshAsync();
        await Search.RefreshAsync();
    }

    private void NotifyNavigation()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private void UpdateModelStatus() =>
        ModelStatus = _embeddings.IsAvailable ? "semantic search ready"
            : _modelLoadCompleted ? "semantic search unavailable"
            : "loading semantic model…";

    /// <summary>Runs event-triggered work; failures are logged and shown instead of crashing.</summary>
    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed");
            _dialogs.ShowError("Something went wrong", ex.Message);
        }
    }
}
