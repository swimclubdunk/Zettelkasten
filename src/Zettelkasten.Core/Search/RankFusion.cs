namespace Zettelkasten.Core.Search;

/// <summary>Reciprocal Rank Fusion: score(d) = Σ 1 / (k + rank). Needs no score calibration.</summary>
public static class RankFusion
{
    public static IReadOnlyList<string> Fuse(IReadOnlyList<IReadOnlyList<string>> rankings, int k = SemanticSettings.RrfK)
    {
        var scores = new Dictionary<string, double>();
        var firstSeen = new List<string>();
        foreach (var ranking in rankings)
        {
            for (var i = 0; i < ranking.Count; i++)
            {
                var id = ranking[i];
                if (!scores.ContainsKey(id)) { scores[id] = 0; firstSeen.Add(id); }
                scores[id] += 1.0 / (k + i + 1);
            }
        }
        // OrderByDescending is stable: ties keep first-seen order
        return firstSeen.OrderByDescending(id => scores[id]).ToList();
    }
}
