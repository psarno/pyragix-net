using FaissNet;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Manages dual indexing: FAISS (vector) + Lucene (BM25 keyword)
/// </summary>
public class IndexService : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private FaissNet.Index? _faissIndex;
    private IndexWriter? _luceneWriter;
    private FSDirectory? _luceneDirectory;

    public IndexService(PyRagixConfig config, PyRagixDbContext dbContext)
    {
        _config = config;
        _dbContext = dbContext;
        InitializeIndexes();
    }

    private void InitializeIndexes()
    {
        // Initialize FAISS index (Flat IP = Inner Product for cosine similarity)
        _faissIndex = FaissNet.Index.CreateDefault(_config.EmbeddingDimension, MetricType.METRIC_INNER_PRODUCT);

        // Initialize Lucene index
        var lucenePath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "lucene_index");
        System.IO.Directory.CreateDirectory(lucenePath);
        _luceneDirectory = FSDirectory.Open(lucenePath);

        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
        _luceneWriter = new IndexWriter(_luceneDirectory, indexConfig);
    }

    /// <summary>
    /// Add a batch of chunks to both FAISS and Lucene indexes
    /// </summary>
    public async Task AddChunksAsync(List<(string content, float[] embedding, ChunkMetadata metadata)> chunks)
    {
        if (_faissIndex == null || _luceneWriter == null)
        {
            throw new InvalidOperationException("Indexes not initialized");
        }

        // Add to SQLite
        foreach (var chunk in chunks)
        {
            _dbContext.Chunks.Add(chunk.metadata);
        }
        await _dbContext.SaveChangesAsync();

        // Add to FAISS with explicit IDs
        var vectors = chunks.Select(c => c.embedding).ToArray();
        var ids = chunks.Select(c => (long)c.metadata.Id).ToArray();
        _faissIndex.AddWithIds(vectors, ids);

        // Add to Lucene (text for BM25)
        foreach (var chunk in chunks)
        {
            var doc = new Document
            {
                new TextField("content", chunk.content, Field.Store.YES),
                new Int32Field("chunk_id", chunk.metadata.Id, Field.Store.YES)
            };
            _luceneWriter.AddDocument(doc);
        }

        _luceneWriter.Commit();
    }

    /// <summary>
    /// Save FAISS index to disk
    /// </summary>
    public void SaveFaissIndex()
    {
        if (_faissIndex == null)
        {
            throw new InvalidOperationException("FAISS index not initialized");
        }

        _faissIndex.Save(_config.FaissIndexPath);
    }

    /// <summary>
    /// Load existing FAISS index from disk
    /// </summary>
    public void LoadFaissIndex()
    {
        if (File.Exists(_config.FaissIndexPath))
        {
            _faissIndex = FaissNet.Index.Load(_config.FaissIndexPath);
        }
    }

    /// <summary>
    /// Get total number of indexed vectors
    /// </summary>
    public long GetIndexSize()
    {
        return _faissIndex?.Count ?? 0;
    }

    public void Dispose()
    {
        _luceneWriter?.Dispose();
        _luceneDirectory?.Dispose();
        _faissIndex?.Dispose();
    }
}
