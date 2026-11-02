using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class SearchQueryTests
{
    [Fact]
    public void Parse_splits_text_tags_and_id_prefix()
    {
        var q = SearchQuery.Parse("  how memory  #Learning #deep-work id:202611 works ");
        Assert.Equal("how memory works", q.FreeText);
        Assert.Equal(new[] { "learning", "deep-work" }, q.Tags);
        Assert.Equal("202611", q.IdPrefix);
        Assert.False(q.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    public void Parse_empty_inputs(string? input)
    {
        Assert.True(SearchQuery.Parse(input).IsEmpty);
    }

    [Fact]
    public void Parse_treats_non_numeric_id_prefix_as_text()
    {
        var q = SearchQuery.Parse("id:abc");
        Assert.Null(q.IdPrefix);
        Assert.Equal("id:abc", q.FreeText);
    }
}
