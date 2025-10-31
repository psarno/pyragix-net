using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;
using System.IO;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Coordinates ingestion of raw documents into chunk metadata, embeddings, and search indexes.
/// Mirrors the Python pipeline: extraction → chunking → embedding → persistence.
/// </summary>
public class IngestionService : IDisposable
{
    private readonly PyRagixConfig _config;
    private PyRagixDbContext _dbContext = null!;
    private readonly DocumentProcessor _documentProcessor;
    private readonly SemanticChunker _chunker;
    private readonly EmbeddingService _embeddingService;
    private IndexService _indexService = null!;

    /// <summary>
    /// Bootstraps all ingestion components up front so repeated calls reuse expensive resources (OCR, ONNX, EF).
    /// </summary>
    public IngestionService(PyRagixConfig config)
    {
        _config = config;
        InitializeStorage();
        _documentProcessor = new DocumentProcessor(config);
        _chunker = new SemanticChunker(config);
        _embeddingService = new EmbeddingService(config);
    }

    /// <summary>
    /// Walks the target folder recursively, processing each supported file into searchable chunks.
    /// </summary>
    public async Task IngestFolderAsync(string folderPath, bool fresh = false)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        Console.WriteLine($"Starting ingestion from: {folderPath}");

        if (fresh)
        {
            Console.WriteLine("Fresh ingestion requested - clearing existing database and index artifacts...");
            ResetStorage();
            Console.WriteLine("Existing artifacts removed. Rebuilding indexes from scratch.");
        }

        // Collect the worklist up front; ingestion order matters because FAISS IDs mirror ChunkMetadata.Id.
        var files = GetSupportedFiles(folderPath);
        Console.WriteLine($"Found {files.Count} files to process");

        // Process each file
        int processedCount = 0;
        foreach (var file in files)
        {
            try
            {
                await IngestFileAsync(file);
                processedCount++;
                Console.WriteLine($"[{processedCount}/{files.Count}] Processed: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {file}: {ex.Message}");
            }
        }

        // Persist FAISS index once everything has been written to SQLite so IDs remain consistent.
        _indexService.SaveFaissIndex();
        Console.WriteLine($"\nIngestion complete! Indexed {_indexService.GetIndexSize()} chunks from {processedCount} files.");
    }

    /// <summary>
    /// Runs the extraction → chunking → embedding → indexing flow for a single file.
    /// </summary>
    private async Task IngestFileAsync(string filePath)
    {
        // Extract the canonical text representation for this document.
        var text = await _documentProcessor.ExtractTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine($"No text extracted from: {filePath}");
            return;
        }

        // Break the document into overlapping sentence-aware chunks so we maintain context in retrieval.
        var chunks = _chunker.ChunkText(text);

        if (chunks.Count == 0)
        {
            Console.WriteLine($"No chunks created from: {filePath}");
            return;
        }

        // Generate vector representations that feed both FAISS and hybrid scoring.
        var embeddings = await _embeddingService.EmbedBatchAsync(chunks);

        // Create metadata rows that capture origin and ordering information for each chunk.
        var chunkData = new List<(string content, float[] embedding, ChunkMetadata metadata)>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var metadata = new ChunkMetadata
            {
                Content = chunks[i],
                SourceFile = filePath,
                FileType = Path.GetExtension(filePath).TrimStart('.'),
                ChunkIndex = i,
                TotalChunks = chunks.Count
            };

            chunkData.Add((chunks[i], embeddings[i], metadata));
        }

        // Persist metadata + vectors to their respective storage engines in one call.
        await _indexService.AddChunksAsync(chunkData);
    }

    /// <summary>
    /// Collects every file under <paramref name="folderPath"/> that the ingestion pipeline understands.
    /// </summary>
    private List<string> GetSupportedFiles(string folderPath)
    {
        var supportedExtensions = new[] { ".pdf", ".html", ".htm", ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".webp" };

        return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    /// <summary>
    /// Drops existing SQLite/FAISS/Lucene artifacts and reinitialises the backing services.
    /// </summary>
    private void ResetStorage()
    {
        _indexService?.Dispose();
        _dbContext?.Dispose();

        DeleteFileIfExists(_config.DatabasePath);
        DeleteFileIfExists(_config.FaissIndexPath);
        DeleteFileIfExists(_config.BM25IndexPath);

        var lucenePath = ResolveLucenePath();
        if (!string.IsNullOrWhiteSpace(lucenePath) && Directory.Exists(lucenePath))
        {
            Directory.Delete(lucenePath, recursive: true);
        }

        InitializeStorage();
    }

    /// <summary>
    /// Ensures the database exists and recreates index services after a reset.
    /// </summary>
    private void InitializeStorage()
    {
        _dbContext = new PyRagixDbContext(_config.DatabasePath);
        _dbContext.EnsureCreated();
        _indexService = new IndexService(_config, _dbContext);
    }

    /// <summary>
    /// Removes the specified file if it exists, normalising relative paths against the working directory.
    /// </summary>
    private void DeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Computes the absolute path to the Lucene index directory based on configuration.
    /// </summary>
    private string ResolveLucenePath()
    {
        var lucenePath = _config.LuceneIndexPath;
        if (string.IsNullOrWhiteSpace(lucenePath))
        {
            return "lucene_index";
        }

        return Path.IsPathRooted(lucenePath)
            ? lucenePath
            : Path.Combine(Directory.GetCurrentDirectory(), lucenePath);
    }

    /// <summary>
    /// Releases native resources (OCR, ONNX, Lucene/FAISS) when ingestion is finished.
    /// </summary>
    public void Dispose()
    {
        _documentProcessor?.Dispose();
        _embeddingService?.Dispose();
        _indexService?.Dispose();
        _dbContext?.Dispose();
    }
}
