using Microsoft.EntityFrameworkCore.Design;

namespace Zettelkasten.Infrastructure.Data;

/// <summary>Used only by `dotnet ef` to create migrations.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(AppDbContext.CreateOptions("Data Source=design.db"));
}
