namespace Zettelkasten.Core.Search;

/// <summary>All tuning knobs for search and semantic matching in one place.</summary>
public static class SemanticSettings
{
    public const int Dimensions = 384;
    public const int MaxTokens = 256;
    public const float SearchMinScore = 0.25f;
    public const float TagSuggestionMinScore = 0.60f;
    /// <summary>"Similar notes" in the side panel: a bit above the search floor; unrelated notes score ~0.12
    /// (tuned against the real model, see RealModelThresholdTests).</summary>
    public const float SimilarNoteMinScore = 0.30f;
    public const int MaxSimilarNotes = 5;
    public const int MaxTagSuggestions = 5;
    public const int RrfK = 60;
    public const int MaxResults = 50;
    public const int CandidateLimit = 100;
}
