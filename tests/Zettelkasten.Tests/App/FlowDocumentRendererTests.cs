using System.Windows;
using System.Windows.Documents;
using Zettelkasten.App.Preview;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public class FlowDocumentRendererTests
{
    private static string TextOf(TextElement e) => new TextRange(e.ContentStart, e.ContentEnd).Text;

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    [Fact]
    public void Renders_heading_and_emphasis() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("# Title\n\nHello *world* and **bold**", _ => null, _ => { });

        var heading = Assert.IsType<Paragraph>(doc.Blocks.FirstBlock);
        Assert.Equal(FontWeights.Bold, heading.FontWeight);
        Assert.Equal("Title", TextOf(heading));
        Assert.Single(FindAll<Italic>(doc));
        Assert.Single(FindAll<Bold>(doc));
    });

    [Fact]
    public void Wiki_link_shows_title_and_reports_click() => StaRunner.Run(() =>
    {
        string? clicked = null;
        var doc = new FlowDocumentRenderer().Render(
            "see [[20261102143015]]", id => id == "20261102143015" ? "Target note" : null, url => clicked = url);

        var link = Assert.Single(FindAll<Hyperlink>(doc));
        Assert.Equal("Target note", TextOf(link));
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Assert.Equal("zk:20261102143015", clicked);
    });

    [Fact]
    public void Broken_link_is_marked() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("[[20261102143015]]", _ => null, _ => { });
        var link = Assert.Single(FindAll<Hyperlink>(doc));
        Assert.Equal(FlowDocumentRenderer.BrokenToolTip, link.ToolTip);
    });

    [Fact]
    public void Renders_lists_quotes_and_code() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("- a\n- b\n\n> quoted\n\n```\nvar x = 1;\n```\n\n---", _ => null, _ => { });
        var blocks = doc.Blocks.ToList();
        Assert.Equal(2, Assert.IsType<List>(blocks[0]).ListItems.Count);
        Assert.IsType<Section>(blocks[1]);
        Assert.Contains("var x = 1;", TextOf(Assert.IsType<Paragraph>(blocks[2])));
        Assert.IsType<BlockUIContainer>(blocks[3]);
    });

    [Fact]
    public void Plain_urls_become_links() => StaRunner.Run(() =>
    {
        string? clicked = null;
        var doc = new FlowDocumentRenderer().Render("visit https://example.org now", _ => null, u => clicked = u);
        var link = Assert.Single(FindAll<Hyperlink>(doc));
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Assert.Equal("https://example.org", clicked);
    });
}
