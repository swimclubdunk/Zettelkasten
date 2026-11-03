namespace Zettelkasten.Core.Graph;

public readonly record struct LayoutPoint(double X, double Y);

/// <summary>Fruchterman–Reingold layout in the unit square with the center note pinned in the middle.
/// Starts from hop rings instead of random positions, so the same graph always looks the same.</summary>
public static class ForceLayout
{
    private const int Iterations = 200;
    private const double Margin = 0.06;

    public static IReadOnlyDictionary<string, LayoutPoint> Compute(LinkGraph graph)
    {
        var nodes = graph.Nodes;
        var n = nodes.Count;
        var index = nodes.Select((node, i) => (node.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var x = new double[n];
        var y = new double[n];

        foreach (var ring in nodes.Select((node, i) => (node, i)).GroupBy(p => p.node.Hop))
        {
            var members = ring.ToList();
            for (var k = 0; k < members.Count; k++)
            {
                var angle = 2 * Math.PI * k / members.Count + ring.Key * 0.5;
                var radius = 0.18 * ring.Key;
                x[members[k].i] = 0.5 + radius * Math.Cos(angle);
                y[members[k].i] = 0.5 + radius * Math.Sin(angle);
            }
        }

        var edges = graph.Edges
            .Select(e => (a: index.GetValueOrDefault(e.SourceId, -1), b: index.GetValueOrDefault(e.TargetId, -1)))
            .Where(e => e.a >= 0 && e.b >= 0).ToList();
        var k0 = 0.7 * Math.Sqrt(1.0 / Math.Max(n, 1));
        var dx = new double[n];
        var dy = new double[n];

        for (var iter = 0; iter < Iterations; iter++)
        {
            var temperature = 0.1 * (1 - (double)iter / Iterations);
            Array.Clear(dx);
            Array.Clear(dy);

            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    var (ux, uy, d) = Delta(x, y, i, j);
                    var force = k0 * k0 / d;
                    dx[i] += ux * force; dy[i] += uy * force;
                    dx[j] -= ux * force; dy[j] -= uy * force;
                }
            foreach (var (a, b) in edges)
            {
                var (ux, uy, d) = Delta(x, y, a, b);
                var force = d * d / k0;
                dx[a] -= ux * force; dy[a] -= uy * force;
                dx[b] += ux * force; dy[b] += uy * force;
            }

            for (var i = 0; i < n; i++)
            {
                if (nodes[i].Hop == 0) continue;
                var length = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (length < 1e-12) continue;
                var step = Math.Min(length, temperature);
                x[i] = Math.Clamp(x[i] + dx[i] / length * step, Margin, 1 - Margin);
                y[i] = Math.Clamp(y[i] + dy[i] / length * step, Margin, 1 - Margin);
            }
        }

        return nodes.Select((node, i) => (node.Id, p: node.Hop == 0 ? new LayoutPoint(0.5, 0.5) : new LayoutPoint(x[i], y[i])))
            .ToDictionary(t => t.Id, t => t.p);
    }

    // Unit vector from j to i and their distance; coincident nodes get a fixed nudge instead of dividing by zero.
    private static (double ux, double uy, double d) Delta(double[] x, double[] y, int i, int j)
    {
        var ddx = x[i] - x[j];
        var ddy = y[i] - y[j];
        var d = Math.Sqrt(ddx * ddx + ddy * ddy);
        if (d < 1e-6) return (Math.Cos(i + j), Math.Sin(i + j), 1e-3);
        return (ddx / d, ddy / d, d);
    }
}
