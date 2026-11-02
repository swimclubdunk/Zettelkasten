using Zettelkasten.Core.Links;

namespace Zettelkasten.Tests.Core;

public class WikiLinkRewriterTests
{
    [Fact]
    public void Rewrite_turns_known_links_into_markdown_links_with_title()
    {
        var md = WikiLinkRewriter.Rewrite("go [[20261102143015]]!", id => "Spaced [rep]");
        Assert.Equal(@"go [Spaced \[rep\]](zk:20261102143015)!", md);
    }

    [Fact]
    public void Rewrite_marks_unknown_links_as_broken()
    {
        var md = WikiLinkRewriter.Rewrite("[[20261102143015]]", _ => null);
        Assert.Equal("[⚠ 20261102143015](zk-broken:20261102143015)", md);
    }

    [Fact]
    public void Rewrite_uses_id_when_title_is_blank()
    {
        var md = WikiLinkRewriter.Rewrite("[[20261102143015]]", _ => "  ");
        Assert.Equal("[20261102143015](zk:20261102143015)", md);
    }
}
