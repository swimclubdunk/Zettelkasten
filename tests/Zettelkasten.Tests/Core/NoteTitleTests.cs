using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Core;

public class NoteTitleTests
{
    [Theory]
    [InlineData("# Spaced repetition\n\nbody", "Spaced repetition")]
    [InlineData("\n\n  ## Deep heading  \nmore", "Deep heading")]
    [InlineData("> quoted idea", "quoted idea")]
    [InlineData("- first bullet\n- second", "first bullet")]
    [InlineData("12. numbered", "numbered")]
    [InlineData("plain first line\nsecond", "plain first line")]
    [InlineData("#hashtag-like start", "#hashtag-like start")]
    [InlineData("\r\n# windows newlines\r\nx", "windows newlines")]
    [InlineData("", "")]
    [InlineData("   \n  \n", "")]
    [InlineData("#\n\nreal text", "real text")]
    public void FromContent_uses_first_meaningful_line(string content, string expected)
    {
        Assert.Equal(expected, NoteTitle.FromContent(content));
    }

    [Fact]
    public void FromContent_truncates_long_lines()
    {
        var title = NoteTitle.FromContent(new string('a', 200));
        Assert.Equal(NoteTitle.MaxLength, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Empty_note_display_name()
    {
        Assert.Equal("(empty note)", new NoteSummary("1", "", DateTimeOffset.Now).DisplayTitle);
    }
}
