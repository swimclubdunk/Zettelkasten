namespace Zettelkasten.Core.Graph;

/// <summary>Breadth-first walk over links in both directions; closer hops win when the node cap is reached.</summary>
public static class LinkNeighborhood
{
    /// <param name="linksTouching">All links whose source or target is one of the given IDs.</param>
    /// <param name="titlesOf">Titles of the given IDs that exist as notes; missing IDs are broken links.</param>
    public static async Task<LinkGraph> BuildAsync(string centerId, int depth, int maxNodes,
        Func<IReadOnlyCollection<string>, Task<IReadOnlyList<GraphEdge>>> linksTouching,
        Func<IReadOnlyCollection<string>, Task<IReadOnlyDictionary<string, string>>> titlesOf)
    {
        var hops = new Dictionary<string, int> { [centerId] = 0 };
        var edges = new HashSet<GraphEdge>();
        var frontier = new List<string> { centerId };
        var truncated = 0;

        for (var hop = 1; hop <= depth && frontier.Count > 0; hop++)
        {
            var links = (await linksTouching(frontier)).Where(l => l.SourceId != l.TargetId).ToList();
            edges.UnionWith(links);
            var candidates = links.SelectMany(l => new[] { l.SourceId, l.TargetId })
                .Where(id => !hops.ContainsKey(id)).Distinct().Order(StringComparer.Ordinal).ToList();
            frontier = [];
            foreach (var id in candidates)
            {
                if (hops.Count >= maxNodes) { truncated++; continue; }
                hops[id] = hop;
                frontier.Add(id);
            }
        }

        var titles = await titlesOf(hops.Keys);
        var nodes = hops.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new GraphNode(kv.Key, titles.GetValueOrDefault(kv.Key), kv.Value, !titles.ContainsKey(kv.Key)))
            .ToList();
        var kept = edges.Where(e => hops.ContainsKey(e.SourceId) && hops.ContainsKey(e.TargetId))
            .OrderBy(e => e.SourceId, StringComparer.Ordinal).ThenBy(e => e.TargetId, StringComparer.Ordinal).ThenBy(e => e.Kind)
            .ToList();
        return new LinkGraph(nodes, kept, truncated);
    }
}
