using Zettelkasten.Core.Tags;

namespace Zettelkasten.Tests.Core;

public class InitialsMatcherTests
{
    [Theory]
    [InlineData("ml", "machine-learning", true)]
    [InlineData("machine-learning", "ml", true)]
    [InlineData("nlp", "natural-language-processing", true)]
    [InlineData("ml", "machine-learning-ops", false)]
    [InlineData("m", "memory", false)]
    [InlineData("ml", "ml", false)]
    [InlineData("ai-ml", "artificial-intelligence", false)]
    public void IsMatch(string a, string b, bool expected)
    {
        Assert.Equal(expected, InitialsMatcher.IsMatch(a, b));
    }
}
