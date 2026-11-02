using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class RankFusionTests
{
    [Fact]
    public void Items_in_both_lists_rank_first()
    {
        var fused = RankFusion.Fuse([["a", "b", "c"], ["c", "d"]]);
        Assert.Equal("c", fused[0]);
        Assert.Equal(new[] { "c", "a", "b", "d" }, fused);
    }

    [Fact]
    public void Single_list_keeps_order_and_empty_lists_are_fine()
    {
        Assert.Equal(new[] { "x", "y" }, RankFusion.Fuse([["x", "y"], []]));
        Assert.Empty(RankFusion.Fuse([]));
    }
}
