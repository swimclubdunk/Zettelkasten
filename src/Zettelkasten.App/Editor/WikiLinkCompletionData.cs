using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.Editor;

/// <summary>An entry in the [[ autocomplete list; typing filters on title or ID.</summary>
public sealed class WikiLinkCompletionData(NoteSummary note) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => $"{note.DisplayTitle} {note.Id}";
    public object Content => $"{note.DisplayTitle}   ·   {note.Id}";
    public object Description => note.Id;
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, $"{note.Id}]]");
}
