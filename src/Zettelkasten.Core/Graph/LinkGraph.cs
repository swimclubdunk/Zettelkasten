using Zettelkasten.Core.Model;

namespace Zettelkasten.Core.Graph;

/// <summary>A note in the local graph. <see cref="Hop"/> is the link distance from the center (0 = the open note).</summary>
public sealed record GraphNode(string Id, string? Title, int Hop, bool IsBroken)
{
    public string DisplayTitle => Title is null ? Id : string.IsNullOrWhiteSpace(Title) ? "(empty note)" : Title;
}

public sealed record GraphEdge(string SourceId, string TargetId, LinkKind Kind);

/// <summary>The open note's neighbourhood. <see cref="Truncated"/> counts notes left out by the node cap.</summary>
public sealed record LinkGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, int Truncated);
