using Zettelkasten.Core.Links;

namespace Zettelkasten.Tests.Core;

public sealed class LinkContextTests
{
    private const string Target = "20261102143015";
    private const string Other = "20261102143016";

    private static string? Titles(string id) => id switch
    {
        Target => "Spaced repetition",
        Other => "Forgetting curve",
        _ => null,
    };

    [Fact]
    public void Returns_the_line_with_the_link_and_renders_links_as_titles()
    {
        var content = $"# Learning\n\nReview with [[{Target}]] to beat the [[{Other}]].\nUnrelated line.";
        Assert.Equal("Review with Spaced repetition to beat the Forgetting curve.",
            LinkContext.Extract(content, Target, Titles));
    }

    [Fact]
    public void Picks_the_sentence_containing_the_link_on_a_long_line()
    {
        var content = $"First sentence is here. Then we use [[{Target}]] daily. Last one follows.";
        Assert.Equal("Then we use Spaced repetition daily.", LinkContext.Extract(content, Target, Titles));
    }

    [Fact]
    public void Strips_markdown_markers()
    {
        var content = $"- **Key:** see `code` and *[[{Target}]]*";
        Assert.Equal("Key: see code and Spaced repetition", LinkContext.Extract(content, Target, Titles));
    }

    [Fact]
    public void Unresolved_links_show_their_id()
    {
        var content = $"[[{Target}]] and [[20990101000000]]";
        Assert.Equal("Spaced repetition and 20990101000000", LinkContext.Extract(content, Target, Titles));
    }

    [Fact]
    public void Long_context_is_trimmed_around_the_link()
    {
        var content = new string('a', 200) + $" [[{Target}]] " + new string('b', 200);
        var context = LinkContext.Extract(content, Target, Titles)!;
        Assert.True(context.Length <= LinkContext.MaxLength);
        Assert.Contains("Spaced repetition", context);
        Assert.StartsWith("…", context);
        Assert.EndsWith("…", context);
    }

    [Fact]
    public void Returns_null_when_the_link_is_absent()
    {
        Assert.Null(LinkContext.Extract("no links here", Target, Titles));
    }
}
