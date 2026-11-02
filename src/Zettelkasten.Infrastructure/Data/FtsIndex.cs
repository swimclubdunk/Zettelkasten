using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Zettelkasten.Infrastructure.Data;

/// <summary>Raw-SQL access to the NotesFts FTS5 table (EF Core has no FTS5 support).</summary>
internal static class FtsIndex
{
    public static async Task UpsertAsync(AppDbContext db, string noteId, string title, string content,
        IEnumerable<string> tags, CancellationToken ct)
    {
        var tagText = string.Join(' ', tags);
        await DeleteAsync(db, noteId, ct);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO NotesFts (NoteId, Title, Content, Tags) VALUES ({noteId}, {title}, {content}, {tagText})", ct);
    }

    public static async Task DeleteAsync(AppDbContext db, string noteId, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"DELETE FROM NotesFts WHERE NoteId = {noteId}", ct);

    /// <summary>
    /// Turns free text into a safe FTS5 expression: every run of letters/digits becomes a quoted
    /// prefix term ("word"*), so user input can never contain FTS syntax. Null when nothing is left.
    /// </summary>
    public static string? BuildMatchExpression(string freeText)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in freeText.Normalize(NormalizationForm.FormC) + " ")
        {
            if (char.IsLetterOrDigit(ch)) { current.Append(ch); continue; }
            if (current.Length > 0) { terms.Add($"\"{current}\"*"); current.Clear(); }
        }
        return terms.Count == 0 ? null : string.Join(' ', terms);
    }

    public static Task<List<string>> MatchAsync(AppDbContext db, string matchExpression, int limit, CancellationToken ct) =>
        db.Database.SqlQuery<string>(
                $"SELECT NoteId AS Value FROM NotesFts WHERE NotesFts MATCH {matchExpression} ORDER BY bm25(NotesFts) LIMIT {limit}")
            .ToListAsync(ct);
}
