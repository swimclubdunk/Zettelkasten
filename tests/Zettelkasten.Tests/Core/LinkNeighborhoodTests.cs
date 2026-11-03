using Zettelkasten.Core.Graph;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Core;

public sealed class LinkNeighborhoodTests
{
    private readonly List<GraphEdge> _links = [];
    private readonly Dictionary<string, string> _titles = [];

    private void Note(params string[] ids) { foreach (var id in ids) _titles[id] = $"title {id}"; }
    private void Link(string from, string to, LinkKind kind = LinkKind.Inline) => _links.Add(new GraphEdge(from, to, kind));

    private Task<LinkGraph> BuildAsync(string center, int depth = 2, int maxNodes = 40) =>
        LinkNeighborhood.BuildAsync(center, depth, maxNodes,
            ids => Task.FromResult<IReadOnlyList<GraphEdge>>(
                _links.Where(l => ids.Contains(l.SourceId) || ids.Contains(l.TargetId)).ToList()),
            ids => Task.FromResult<IReadOnlyDictionary<string, string>>(
                _titles.Where(kv => ids.Contains(kv.Key)).ToDictionary()));

    [Fact]
    public async Task Walks_links_in_both_directions_up_to_the_depth()
    {
        Note("c", "out", "in", "far", "farther");
        Link("c", "out");
        Link("in", "c", LinkKind.Manual);
        Link("out", "far");
        Link("far", "farther");

        var g = await BuildAsync("c");

        Assert.Equal(new Dictionary<string, int> { ["c"] = 0, ["in"] = 1, ["out"] = 1, ["far"] = 2 },
            g.Nodes.ToDictionary(n => n.Id, n => n.Hop));
        Assert.Contains(new GraphEdge("in", "c", LinkKind.Manual), g.Edges);
        Assert.Contains(new GraphEdge("out", "far", LinkKind.Inline), g.Edges);
        Assert.DoesNotContain(g.Edges, e => e.TargetId == "farther");
        Assert.Equal("title out", g.Nodes.Single(n => n.Id == "out").Title);
        Assert.Equal(0, g.Truncated);
    }

    [Fact]
    public async Task Missing_targets_are_broken_nodes()
    {
        Note("c");
        Link("c", "ghost");
        var ghost = (await BuildAsync("c")).Nodes.Single(n => n.Id == "ghost");
        Assert.True(ghost.IsBroken);
        Assert.Null(ghost.Title);
    }

    [Fact]
    public async Task Caps_the_node_count_keeping_closer_hops_and_reports_the_rest()
    {
        Note("c", "a", "b", "a1", "a2");
        Link("c", "a"); Link("c", "b"); Link("a", "a1"); Link("a", "a2");

        var g = await BuildAsync("c", maxNodes: 4);

        Assert.Equal(4, g.Nodes.Count);
        Assert.Contains(g.Nodes, n => n.Id == "a");
        Assert.Contains(g.Nodes, n => n.Id == "b");
        Assert.Equal(1, g.Truncated);
        Assert.All(g.Edges, e => Assert.Contains(g.Nodes, n => n.Id == e.TargetId));
    }

    [Fact]
    public async Task Duplicate_and_self_links_collapse()
    {
        Note("c", "a");
        Link("c", "a"); Link("c", "a"); Link("c", "c");
        var g = await BuildAsync("c");
        Assert.Single(g.Edges);
        Assert.Equal(2, g.Nodes.Count);
    }

    [Fact]
    public async Task A_note_without_links_is_a_lone_center()
    {
        Note("c");
        var g = await BuildAsync("c");
        Assert.Equal("c", Assert.Single(g.Nodes).Id);
        Assert.Empty(g.Edges);
    }
}
