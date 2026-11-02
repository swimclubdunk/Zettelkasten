using Zettelkasten.Core.Ids;

namespace Zettelkasten.Tests.Core;

public class NoteIdGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 14, 30, 15, 789, TimeSpan.FromHours(1));

    [Fact]
    public void Generate_formats_local_time_to_the_second()
    {
        Assert.Equal("20261102143015", NoteIdGenerator.Generate(Now, _ => false));
    }

    [Fact]
    public void Generate_skips_taken_ids_by_one_second()
    {
        var taken = new HashSet<string> { "20261102143015", "20261102143016" };
        Assert.Equal("20261102143017", NoteIdGenerator.Generate(Now, taken.Contains));
    }

    [Theory]
    [InlineData("20261102143015", true)]
    [InlineData("2026110214301", false)]
    [InlineData("2026110214301a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_checks_fourteen_ascii_digits(string? id, bool expected)
    {
        Assert.Equal(expected, NoteIdGenerator.IsValid(id));
    }
}
