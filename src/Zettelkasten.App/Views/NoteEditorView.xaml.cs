using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Zettelkasten.App.Editor;
using Zettelkasten.App.Preview;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Links;

namespace Zettelkasten.App.Views;

public partial class NoteEditorView : UserControl
{
    private readonly FlowDocumentRenderer _renderer;
    private NoteEditorViewModel? _vm;
    private CompletionWindow? _completion;
    private bool _syncing;

    public NoteEditorView()
    {
        InitializeComponent();
        _renderer = new FlowDocumentRenderer(
            (Brush)FindResource("AccentBrush"), (Brush)FindResource("ErrorBrush"), (Brush)FindResource("CodeBackgroundBrush"));
        Editor.SyntaxHighlighting = LoadHighlighting();
        Editor.TextChanged += (_, _) =>
        {
            if (!_syncing && _vm is not null) _vm.Content = Editor.Text;
        };
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.PreviewMouseLeftButtonDown += OnEditorMouseDown;
        DataContextChanged += OnDataContextChanged;
    }

    internal static IHighlightingDefinition LoadHighlighting()
    {
        using var stream = typeof(NoteEditorView).Assembly.GetManifestResourceStream("Markdown.xshd")
                           ?? throw new InvalidOperationException("Markdown.xshd resource is missing.");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    public void FocusTitle()
    {
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.NoteLoaded -= OnNoteLoaded;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _vm = e.NewValue as NoteEditorViewModel;
        if (_vm is not null)
        {
            _vm.NoteLoaded += OnNoteLoaded;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        OnNoteLoaded(this, EventArgs.Empty);
    }

    private void OnNoteLoaded(object? sender, EventArgs e)
    {
        _completion?.Close();
        _syncing = true;
        try
        {
            Editor.Text = _vm?.Content ?? "";
            Editor.Document.UndoStack.ClearAll(); // Ctrl+Z must not bring back the previous note
            Editor.ScrollToHome();
        }
        finally
        {
            _syncing = false;
        }
        if (_vm?.IsPreview == true) RenderPreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteEditorViewModel.IsPreview) && _vm?.IsPreview == true) RenderPreview();
    }

    private async void RenderPreview()
    {
        if (_vm is null) return;
        var titles = await _vm.GetLinkTitlesAsync();
        Preview.Document = _renderer.Render(_vm.Content, id => titles.GetValueOrDefault(id), OnPreviewLink);
    }

    private void OnPreviewLink(string url)
    {
        if (url.StartsWith(WikiLinkRewriter.NoteScheme, StringComparison.Ordinal))
            _vm?.RequestOpen(url[WikiLinkRewriter.NoteScheme.Length..]);
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text != "[" || _vm is null) return;
        var offset = Editor.CaretOffset;
        if (offset < 2 || Editor.Document.GetText(offset - 2, 2) != "[[") return;

        var candidates = await _vm.GetLinkCandidatesAsync();
        if (candidates.Count == 0) return;

        _completion = new CompletionWindow(Editor.TextArea) { Width = 380 };
        foreach (var note in candidates)
            _completion.CompletionList.CompletionData.Add(new WikiLinkCompletionData(note));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null) return;
        var offset = Editor.Document.GetOffset(position.Value.Location);
        if (LinkParser.FindAt(Editor.Text, offset) is { } link)
        {
            _vm.RequestOpen(link.TargetId);
            e.Handled = true;
        }
    }
}
