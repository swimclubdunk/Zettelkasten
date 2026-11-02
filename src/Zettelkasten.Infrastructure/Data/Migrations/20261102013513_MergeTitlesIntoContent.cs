using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettelkasten.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MergeTitlesIntoContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Notes no longer have titles: keep each existing title as a first-line heading of the content.
            // DbInitializer then recomputes the cached Title column and the search index from the new content.
            migrationBuilder.Sql(
                "UPDATE Notes SET Content = CASE WHEN Content = '' THEN '# ' || trim(Title) " +
                "ELSE '# ' || trim(Title) || char(10) || char(10) || Content END " +
                "WHERE trim(Title) <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the headings stay in the content, which is harmless for the previous version.
        }
    }
}
