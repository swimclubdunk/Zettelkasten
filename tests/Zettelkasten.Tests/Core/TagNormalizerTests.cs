using Zettelkasten.Core.Tags;

namespace Zettelkasten.Tests.Core;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("Machine Learning", "machine-learning")]
    [InlineData("  machine   learning  ", "machine-learning")]
    [InlineData("#Memory", "memory")]
    [InlineData("a - b", "a-b")]
    [InlineData("--x--", "x")]
    [InlineData("   ", "")]
    [InlineData("#", "")]
    public void Normalize_basic(string input, string expected)
    {
        Assert.Equal(expected, TagNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_handles_unicode()
    {
        Assert.Equal("café-society", TagNormalizer.Normalize("  Café Society "));
        // decomposed e + combining acute must equal the composed form
        Assert.Equal(TagNormalizer.Normalize("Café"), TagNormalizer.Normalize("Café"));
        Assert.Equal("🧠-brain", TagNormalizer.Normalize("🧠 Brain"));
    }
}
