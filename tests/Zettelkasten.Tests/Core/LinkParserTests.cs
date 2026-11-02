using Zettelkasten.Core.Links;

namespace Zettelkasten.Tests.Core;

public class LinkParserTests
{
    [Fact]
    public void ExtractTargets_returns_distinct_ids_in_order()
    {
        var text = "See [[20261102143015]] and [[20261030091200]], again [[20261102143015]].";
        Assert.Equal(new[] { "20261102143015", "20261030091200" }, LinkParser.ExtractTargets(text));
    }

    [Theory]
    [InlineData("[[123]]")]
    [InlineData("[[2026110214301a]]")]
    [InlineData("[20261102143015]")]
    [InlineData("[[ 20261102143015 ]]")]
    [InlineData("[[202611021430150]]")]
    public void ExtractTargets_ignores_invalid_links(string text)
    {
        Assert.Empty(LinkParser.ExtractTargets(text));
    }

    [Fact]
    public void Matches_reports_positions()
    {
        var m = Assert.Single(LinkParser.Matches("ab [[20261102143015]] cd"));
        Assert.Equal(3, m.Start);
        Assert.Equal(18, m.Length);
        Assert.Equal("20261102143015", m.TargetId);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(10, true)]
    [InlineData(21, true)]
    [InlineData(2, false)]
    [InlineData(22, false)]
    public void FindAt_finds_link_under_offset(int offset, bool found)
    {
        var result = LinkParser.FindAt("ab [[20261102143015]] cd", offset);
        Assert.Equal(found, result is not null);
    }
}
