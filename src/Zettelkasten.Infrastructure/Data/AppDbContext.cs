using Microsoft.EntityFrameworkCore;

namespace Zettelkasten.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<NoteTagEntity> NoteTags => Set<NoteTagEntity>();
    public DbSet<NoteLinkEntity> NoteLinks => Set<NoteLinkEntity>();
    public DbSet<NoteEmbeddingEntity> NoteEmbeddings => Set<NoteEmbeddingEntity>();

    public static DbContextOptions<AppDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<NoteEntity>(e =>
        {
            e.ToTable("Notes");
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.UpdatedAtMs);
            e.HasOne(n => n.Embedding).WithOne()
                .HasForeignKey<NoteEmbeddingEntity>(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TagEntity>(e =>
        {
            e.ToTable("Tags");
            e.Property(t => t.Name).UseCollation("NOCASE");
            e.HasIndex(t => t.Name).IsUnique();
        });

        b.Entity<NoteTagEntity>(e =>
        {
            e.ToTable("NoteTags");
            e.HasKey(x => new { x.NoteId, x.TagId });
            e.HasOne(x => x.Note).WithMany(n => n.NoteTags).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.NoteTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NoteLinkEntity>(e =>
        {
            e.ToTable("NoteLinks");
            e.HasKey(x => new { x.SourceId, x.TargetId, x.Kind });
            e.HasIndex(x => x.TargetId);
            e.HasOne<NoteEntity>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NoteEmbeddingEntity>(e =>
        {
            e.ToTable("NoteEmbeddings");
            e.HasKey(x => x.NoteId);
        });
    }
}
