using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Zettelkasten.App.Editor;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Model;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public class EditorSupportTests
{
    [Fact]
    public void Completion_inserts_id_and_closing_brackets() => StaRunner.Run(() =>
    {
        var editor = new TextEditor { Text = "see [[mem" };
        var data = new WikiLinkCompletionData(new NoteSummary("20261102143015", "Memory", DateTimeOffset.Now));

        data.Complete(editor.TextArea, new TextSegment { StartOffset = 6, Length = 3 }, EventArgs.Empty);

        Assert.Equal("see [[20261102143015]]", editor.Text);
        Assert.Contains("Memory", data.Text);
        Assert.Contains("20261102143015", data.Text);
    });

    [Fact]
    public void Markdown_highlighting_loads() => StaRunner.Run(() =>
    {
        var definition = NoteEditorView.LoadHighlighting();
        Assert.Equal("Markdown", definition.Name);
        Assert.NotNull(definition.GetNamedColor("WikiLink"));
    });
}
