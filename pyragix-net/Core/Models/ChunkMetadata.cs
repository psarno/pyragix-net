using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PyRagix.Net.Core.Models;

/// <summary>
/// SQLite entity representing a single chunk produced during ingestion.
/// The primary key is reused as the vector identifier inside FAISS to keep lookups aligned.
/// </summary>
public class ChunkMetadata
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The actual text content of the chunk
    /// </summary>
    [Required]
    public required string Content { get; set; }

    /// <summary>
    /// Source file path (relative or absolute)
    /// </summary>
    [Required]
    public required string SourceFile { get; set; }

    /// <summary>
    /// File type extension (e.g., "pdf", "html", "png")
    /// </summary>
    [Required]
    public required string FileType { get; set; }

    /// <summary>
    /// Zero-based chunk index within the source document
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Total number of chunks in the source document
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Timestamp when chunk was created/ingested
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Embedding vector (stored as comma-separated string for SQLite compatibility)
    /// Actual vector stored in FAISS index
    /// </summary>
    public string? VectorHash { get; set; }
}
