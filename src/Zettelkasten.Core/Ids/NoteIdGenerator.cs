using System.Globalization;

namespace Zettelkasten.Core.Ids;

/// <summary>Creates Zettelkasten IDs from the local creation time (yyyyMMddHHmmss).</summary>
public static class NoteIdGenerator
{
    public const string Format = "yyyyMMddHHmmss";

    public static string Generate(DateTimeOffset localNow, Func<string, bool> exists)
    {
        var t = new DateTime(localNow.Year, localNow.Month, localNow.Day,
            localNow.Hour, localNow.Minute, localNow.Second);
        while (true)
        {
            var id = t.ToString(Format, CultureInfo.InvariantCulture);
            if (!exists(id)) return id;
            t = t.AddSeconds(1);
        }
    }

    public static bool IsValid(string? id) => id is { Length: 14 } && id.All(char.IsAsciiDigit);
}
