using Microsoft.EntityFrameworkCore;
using PyRagix.Net.Core.Models;

namespace PyRagix.Net.Core.Data;

/// <summary>
/// SQLite database context for chunk metadata storage
/// </summary>
public class PyRagixDbContext : DbContext
{
    private readonly string _databasePath;

    public DbSet<ChunkMetadata> Chunks { get; set; } = null!;

    public PyRagixDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChunkMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SourceFile);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.SourceFile).IsRequired();
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
        });
    }

    /// <summary>
    /// Ensure database and tables are created
    /// </summary>
    public void EnsureCreated()
    {
        Database.EnsureCreated();
    }
}
