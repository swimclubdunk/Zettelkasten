using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zettelkasten.App.Services;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Graph;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Tags;

namespace Zettelkasten.App.ViewModels;

public sealed partial class NotePanelViewModel : ObservableObject, IDisposable
{
    private readonly INoteRepository _notes;
    private readonly ITagRepository _tags;
    private readonly ITagSuggestionService _suggestions;
    private readonly ISimilarNotesService _similar;
    private readonly IEmbeddingService _embeddings;
    private readonly NoteEditorViewModel _editor;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _suggestDebounce;

    public NotePanelViewModel(INoteRepository notes, ITagRepository tags, ITagSuggestionService suggestions,
        ISimilarNotesService similar, IEmbeddingService embeddings, NoteEditorViewModel editor, IDialogService dialogs, TimeProvider time)
    {
        _notes = notes;
        _tags = tags;
        _suggestions = suggestions;
        _similar = similar;
        _embeddings = embeddings;
        _editor = editor;
        _dialogs = dialogs;
        _suggestDebounce = new Debouncer(TimeSpan.FromMilliseconds(200), time);
    }

    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<LinkInfo> Related { get; } = [];
    public ObservableCollection<LinkInfo> Outgoing { get; } = [];
    public ObservableCollection<LinkInfo> Backlinks { get; } = [];
    public ObservableCollection<TagSuggestion> Suggestions { get; } = [];
    public ObservableCollection<SimilarNote> Similar { get; } = [];

    [ObservableProperty] private string _tagInput = "";
    [ObservableProperty] private bool _canCreateTag;
    [ObservableProperty] private bool _isSemanticAvailable;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGraphLinks))]
    private LinkGraph? _graph;
    [ObservableProperty] private bool _isGraphExpanded = true;

    public bool HasGraphLinks => Graph is { Edges.Count: > 0 };

    internal Task PendingSuggest { get; private set; } = Task.CompletedTask;

    public event EventHandler<string>? OpenRequested;

    private string NormalizedInput => TagNormalizer.Normalize(TagInput);

    public async Task RefreshAsync()
    {
        var note = _editor.CurrentNote;
        Replace(Tags, note?.Tags ?? []);
        if (note is null)
        {
            Related.Clear(); Outgoing.Clear(); Backlinks.Clear(); Similar.Clear();
            Graph = null;
            return;
        }
        var outgoing = await _notes.GetOutgoingLinksAsync(note.Id);
        var backlinks = await _notes.GetBacklinksAsync(note.Id);
        Replace(Related, outgoing.Where(l => l.Kind == LinkKind.Manual));
        Replace(Outgoing, outgoing.Where(l => l.Kind == LinkKind.Inline));
        Replace(Backlinks, backlinks);
        Graph = await _notes.GetNeighborhoodAsync(note.Id);

        IsSemanticAvailable = _embeddings.IsAvailable;
        Replace(Similar, IsSemanticAvailable ? await _similar.FindAsync(note.Id) : []);
    }

    partial void OnTagInputChanged(string value)
    {
        CanCreateTag = false;
        PendingSuggest = _suggestDebounce.Trigger(async () => ShowSuggestions(await _suggestions.SuggestAsync(TagInput)));
    }

    [RelayCommand]
    private async Task AddTagFromInput()
    {
        var name = NormalizedInput;
        if (name.Length == 0) return;
        var result = await _suggestions.SuggestAsync(TagInput);
        if (result.ExactMatch is not null) { await AddTagAsync(result.ExactMatch.Name); return; }
        if (result.Similar.Count > 0) { ShowSuggestions(result); return; } // let the user decide
        await AddTagAsync(name);
    }

    [RelayCommand]
    private Task AcceptSuggestion(TagSuggestion suggestion) => AddTagAsync(suggestion.Tag.Name);

    [RelayCommand]
    private Task CreateTagAnyway() => AddTagAsync(NormalizedInput);

    [RelayCommand]
    private async Task RemoveTag(string name)
    {
        await _editor.UpdateAndSaveAsync(n => n.Tags.Remove(name));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteTagEverywhere(string name)
    {
        if (!_dialogs.Confirm("Delete tag", $"Remove the tag \"{name}\" from all notes and delete it?\n\nThis cannot be undone."))
            return;
        var tag = (await _tags.GetAllAsync()).FirstOrDefault(t => t.Name == name);
        if (tag is null) return;
        await _editor.FlushAsync();
        await _tags.DeleteAsync(tag.Id);
        _editor.CurrentNote?.Tags.Remove(name);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddManualLink()
    {
        var note = _editor.CurrentNote;
        if (note is null) return;
        var id = _dialogs.PickNote("Link to note", note.Id);
        if (id is null || id == note.Id) return;
        await _editor.UpdateAndSaveAsync(n => { if (!n.ManualLinks.Contains(id)) n.ManualLinks.Add(id); });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveManualLink(LinkInfo link)
    {
        await _editor.UpdateAndSaveAsync(n => n.ManualLinks.Remove(link.NoteId));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task LinkSimilar(SimilarNote similar)
    {
        var id = similar.Note.Id;
        await _editor.UpdateAndSaveAsync(n => { if (!n.ManualLinks.Contains(id)) n.ManualLinks.Add(id); });
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenSimilar(SimilarNote similar) => OpenRequested?.Invoke(this, similar.Note.Id);

    [RelayCommand]
    private void OpenGraphNode(GraphNode node)
    {
        if (!node.IsBroken && node.Hop > 0) OpenRequested?.Invoke(this, node.Id);
    }

    [RelayCommand]
    private void ToggleGraph() => IsGraphExpanded = !IsGraphExpanded;

    [RelayCommand]
    private void OpenLink(LinkInfo link)
    {
        if (!link.IsBroken) OpenRequested?.Invoke(this, link.NoteId);
    }

    private async Task AddTagAsync(string name)
    {
        if (name.Length == 0 || _editor.CurrentNote is null) return;
        await _editor.UpdateAndSaveAsync(n => { if (!n.Tags.Contains(name)) n.Tags.Add(name); });
        TagInput = "";
        _suggestDebounce.Cancel();
        Suggestions.Clear();
        CanCreateTag = false;
        await RefreshAsync();
    }

    private void ShowSuggestions(TagSuggestions result)
    {
        Suggestions.Clear();
        if (result.ExactMatch is not null)
            Suggestions.Add(new TagSuggestion(result.ExactMatch, 1f, false));
        else
            foreach (var s in result.Similar) Suggestions.Add(s);
        var name = NormalizedInput;
        CanCreateTag = result.ExactMatch is null && name.Length > 0 && !Tags.Contains(name);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose() => _suggestDebounce.Dispose();
}
