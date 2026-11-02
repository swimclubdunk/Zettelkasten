using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Tests.Infrastructure;

public class FtsIndexTests
{
    [Theory]
    [InlineData("memory", "\"memory\"*")]
    [InlineData("how  memory", "\"how\"* \"memory\"*")]
    [InlineData("e-mail \"quoted", "\"e\"* \"mail\"* \"quoted\"*")]
    [InlineData("NOT AND NEAR", "\"NOT\"* \"AND\"* \"NEAR\"*")]
    [InlineData("Café", "\"Café\"*")]
    public void BuildMatchExpression_quotes_every_token(string input, string expected)
    {
        Assert.Equal(expected, FtsIndex.BuildMatchExpression(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*:-()\"")]
    public void BuildMatchExpression_returns_null_without_tokens(string input)
    {
        Assert.Null(FtsIndex.BuildMatchExpression(input));
    }
}
