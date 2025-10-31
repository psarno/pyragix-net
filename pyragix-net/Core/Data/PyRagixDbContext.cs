using Microsoft.EntityFrameworkCore;
using PyRagix.Net.Core.Models;

namespace PyRagix.Net.Core.Data;

/// <summary>
/// Minimal EF Core context used to persist chunk metadata and mirror the SQLite layout from the Python project.
/// </summary>
public class PyRagixDbContext : DbContext
{
    private readonly string _databasePath;

    /// <summary>
    /// Table containing one row per chunk generated during ingestion.
    /// </summary>
    public DbSet<ChunkMetadata> Chunks { get; set; } = null!;

    /// <summary>
    /// Captures the target SQLite file path so it can be supplied to <see cref="DbContextOptionsBuilder"/>.
    /// </summary>
    public PyRagixDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Point EF Core at the caller-specified database file so local runs can isolate datasets.
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChunkMetadata>(entity =>
        {
            // Keep columns aligned with the Python ingestion schema for parity with existing data.
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SourceFile);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.SourceFile).IsRequired();
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
        });
    }

    /// <summary>
    /// Ensures the backing SQLite file and table exist before we attempt to insert chunk metadata.
    /// </summary>
    public void EnsureCreated()
    {
        Database.EnsureCreated();
    }
}
