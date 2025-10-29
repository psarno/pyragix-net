using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Main ingestion pipeline: Load → Process → Chunk → Embed → Index
/// </summary>
public class IngestionService : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private readonly DocumentProcessor _documentProcessor;
    private readonly SemanticChunker _chunker;
    private readonly EmbeddingService _embeddingService;
    private readonly IndexService _indexService;

    public IngestionService(PyRagixConfig config)
    {
        _config = config;
        _dbContext = new PyRagixDbContext(config.DatabasePath);
        _dbContext.EnsureCreated();

        _documentProcessor = new DocumentProcessor(config);
        _chunker = new SemanticChunker(config);
        _embeddingService = new EmbeddingService(config);
        _indexService = new IndexService(config, _dbContext);
    }

    /// <summary>
    /// Ingest all documents from a folder
    /// </summary>
    public async Task IngestFolderAsync(string folderPath, bool fresh = false)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        Console.WriteLine($"Starting ingestion from: {folderPath}");

        // Get all supported files
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

        // Save indexes
        _indexService.SaveFaissIndex();
        Console.WriteLine($"\nIngestion complete! Indexed {_indexService.GetIndexSize()} chunks from {processedCount} files.");
    }

    /// <summary>
    /// Ingest a single file
    /// </summary>
    private async Task IngestFileAsync(string filePath)
    {
        // Extract text
        var text = await _documentProcessor.ExtractTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine($"No text extracted from: {filePath}");
            return;
        }

        // Chunk text
        var chunks = _chunker.ChunkText(text);

        if (chunks.Count == 0)
        {
            Console.WriteLine($"No chunks created from: {filePath}");
            return;
        }

        // Generate embeddings
        var embeddings = await _embeddingService.EmbedBatchAsync(chunks);

        // Create metadata
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

        // Add to indexes
        await _indexService.AddChunksAsync(chunkData);
    }

    /// <summary>
    /// Get all supported files from folder (recursive)
    /// </summary>
    private List<string> GetSupportedFiles(string folderPath)
    {
        var supportedExtensions = new[] { ".pdf", ".html", ".htm", ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".webp" };

        return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    public void Dispose()
    {
        _documentProcessor?.Dispose();
        _embeddingService?.Dispose();
        _indexService?.Dispose();
        _dbContext?.Dispose();
    }
}
