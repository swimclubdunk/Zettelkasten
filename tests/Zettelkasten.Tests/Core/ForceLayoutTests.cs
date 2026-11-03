using Zettelkasten.Core.Graph;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Core;

public sealed class ForceLayoutTests
{
    private static LinkGraph Star(int leaves)
    {
        var nodes = new List<GraphNode> { new("c", "center", 0, false) };
        var edges = new List<GraphEdge>();
        for (var i = 0; i < leaves; i++)
        {
            nodes.Add(new GraphNode($"n{i}", $"leaf {i}", 1, false));
            edges.Add(new GraphEdge("c", $"n{i}", LinkKind.Inline));
            nodes.Add(new GraphNode($"m{i}", $"far {i}", 2, false));
            edges.Add(new GraphEdge($"n{i}", $"m{i}", LinkKind.Inline));
        }
        return new LinkGraph(nodes, edges, 0);
    }

    [Fact]
    public void Is_deterministic()
    {
        Assert.Equal(ForceLayout.Compute(Star(6)), ForceLayout.Compute(Star(6)));
    }

    [Fact]
    public void Pins_the_center_and_keeps_every_node_inside_the_unit_square()
    {
        var pos = ForceLayout.Compute(Star(10));
        Assert.Equal(new LayoutPoint(0.5, 0.5), pos["c"]);
        Assert.All(pos.Values, p => Assert.InRange(p.X, 0, 1));
        Assert.All(pos.Values, p => Assert.InRange(p.Y, 0, 1));
    }

    [Fact]
    public void Spreads_nodes_apart()
    {
        var pos = ForceLayout.Compute(Star(8)).Values.ToList();
        var min = double.MaxValue;
        for (var i = 0; i < pos.Count; i++)
            for (var j = i + 1; j < pos.Count; j++)
                min = Math.Min(min, Math.Sqrt(Math.Pow(pos[i].X - pos[j].X, 2) + Math.Pow(pos[i].Y - pos[j].Y, 2)));
        Assert.True(min > 0.05, $"closest pair at {min:0.000}");
    }

    [Fact]
    public void Direct_neighbours_sit_closer_to_the_center_than_second_hops()
    {
        var pos = ForceLayout.Compute(Star(5));
        double Dist(string id) => Math.Sqrt(Math.Pow(pos[id].X - 0.5, 2) + Math.Pow(pos[id].Y - 0.5, 2));
        var hop1 = Enumerable.Range(0, 5).Average(i => Dist($"n{i}"));
        var hop2 = Enumerable.Range(0, 5).Average(i => Dist($"m{i}"));
        Assert.True(hop1 < hop2, $"hop1 {hop1:0.000}, hop2 {hop2:0.000}");
    }

    [Fact]
    public void Handles_a_lone_node()
    {
        var g = new LinkGraph([new GraphNode("c", "only", 0, false)], [], 0);
        Assert.Equal(new LayoutPoint(0.5, 0.5), ForceLayout.Compute(g)["c"]);
    }
}
