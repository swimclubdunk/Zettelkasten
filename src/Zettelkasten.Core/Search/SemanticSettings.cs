namespace Zettelkasten.Core.Search;

/// <summary>All tuning knobs for search and semantic matching in one place.</summary>
public static class SemanticSettings
{
    public const int Dimensions = 384;
    public const int MaxTokens = 256;
    public const float SearchMinScore = 0.35f;
    public const float TagSuggestionMinScore = 0.75f;
    public const int MaxTagSuggestions = 5;
    public const int RrfK = 60;
    public const int MaxResults = 50;
    public const int CandidateLimit = 100;
}
