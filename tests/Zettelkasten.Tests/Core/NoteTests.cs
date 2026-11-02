using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Core;

public class NoteTests
{
    [Fact]
    public void Clone_copies_lists_independently()
    {
        var n = new Note { Id = "20261102143015", Content = "t", Tags = ["a"], ManualLinks = ["20261102143016"] };
        var c = n.Clone();
        c.Tags.Add("b");
        c.ManualLinks.Clear();
        Assert.Equal(new[] { "a" }, n.Tags);
        Assert.Single(n.ManualLinks);
        Assert.Equal("t", c.Title);
    }

    [Fact]
    public void Display_titles_fall_back()
    {
        Assert.Equal("(empty note)", new NoteSummary("1", " ", DateTimeOffset.Now).DisplayTitle);
        Assert.True(new LinkInfo("20261102143015", null, LinkKind.Inline).IsBroken);
        Assert.Equal("20261102143015", new LinkInfo("20261102143015", null, LinkKind.Inline).DisplayTitle);
    }
}
