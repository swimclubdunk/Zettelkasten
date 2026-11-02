using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class WordPieceTokenizerTests
{
    // index = token id
    private static readonly WordPieceTokenizer Tok = new(
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "hello", "world", "play", "##ing", "!", "cafe", ","]);

    [Fact]
    public void Encodes_words_and_punctuation_with_special_tokens()
    {
        Assert.Equal(new long[] { 2, 4, 10, 5, 8, 3 }, Tok.Encode("Hello, world!"));
    }

    [Fact]
    public void Splits_into_word_pieces()
    {
        Assert.Equal(new long[] { 2, 6, 7, 3 }, Tok.Encode("playing"));
    }

    [Fact]
    public void Strips_accents_and_lowercases()
    {
        Assert.Equal(new long[] { 2, 9, 3 }, Tok.Encode("CAFÉ"));
    }

    [Fact]
    public void Unknown_words_become_unk()
    {
        Assert.Equal(new long[] { 2, 1, 4, 3 }, Tok.Encode("xyz hello"));
    }

    [Fact]
    public void Truncates_to_max_tokens_keeping_sep()
    {
        var ids = Tok.Encode(string.Concat(Enumerable.Repeat("hello ", 300)), maxTokens: 10);
        Assert.Equal(10, ids.Length);
        Assert.Equal(2, ids[0]);
        Assert.Equal(3, ids[^1]);
    }

    [Fact]
    public void Empty_text_is_cls_sep()
    {
        Assert.Equal(new long[] { 2, 3 }, Tok.Encode("  \t\n "));
    }
}
