using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;
using PyRagix.Net.Ingestion.Vector;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Persists chunk content to SQLite, FAISS, and Lucene so retrieval can perform hybrid semantic + keyword search.
/// Maintains the one-to-one relationship between database IDs and FAISS vector IDs.
/// </summary>
public class IndexService : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private readonly IVectorIndexFactory _vectorIndexFactory;
    private IVectorIndex? _vectorIndex;
    private IndexWriter? _luceneWriter;
    private FSDirectory? _luceneDirectory;

    /// <summary>
    /// Creates the index handles eagerly so ingestion can reuse them for the lifetime of the service.
    /// </summary>
    public IndexService(PyRagixConfig config, PyRagixDbContext dbContext, IVectorIndexFactory? vectorIndexFactory = null)
    {
        _config = config;
        _dbContext = dbContext;
        _vectorIndexFactory = vectorIndexFactory ?? VectorIndexFactoryResolver.GetDefault();
        InitializeIndexes();
    }

    /// <summary>
    /// Sets up the FAISS and Lucene handles so ingestion can immediately start writing.
    /// </summary>
    private void InitializeIndexes()
    {
        // Initialize FAISS index (Flat IP = Inner Product for cosine similarity)
        _vectorIndex = _vectorIndexFactory.Create(_config.EmbeddingDimension);

        // Initialize Lucene index
        var lucenePath = ResolveLucenePath();
        System.IO.Directory.CreateDirectory(lucenePath);
        _luceneDirectory = FSDirectory.Open(lucenePath);

        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
        _luceneWriter = new IndexWriter(_luceneDirectory, indexConfig);
    }

    /// <summary>
    /// Adds the provided chunk batch to SQLite, FAISS, and Lucene so all stores remain in sync.
    /// </summary>
    public async Task AddChunksAsync(List<(string content, float[] embedding, ChunkMetadata metadata)> chunks)
    {
        if (_vectorIndex == null || _luceneWriter == null)
        {
            throw new InvalidOperationException("Indexes not initialized");
        }

        // Add to SQLite first so we get the auto-incremented IDs required by FAISS.
        foreach (var chunk in chunks)
        {
            _dbContext.Chunks.Add(chunk.metadata);
        }
        await _dbContext.SaveChangesAsync();

        // Add to FAISS with explicit IDs
        var vectors = chunks.Select(c => c.embedding).ToArray();
        var ids = chunks.Select(c => (long)c.metadata.Id).ToArray();
        _vectorIndex.AddWithIds(vectors, ids);

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
    /// Persists the in-memory FAISS index to disk so future retrieval runs can load it without re-ingestion.
    /// </summary>
    public void SaveVectorIndex()
    {
        if (_vectorIndex == null)
        {
            throw new InvalidOperationException("Vector index not initialized");
        }

        _vectorIndex.Save(_config.FaissIndexPath);
    }

    /// <summary>
    /// Rehydrates the vector index from disk, replacing any in-memory instance.
    /// </summary>
    public void LoadVectorIndex()
    {
        if (File.Exists(_config.FaissIndexPath))
        {
            _vectorIndex?.Dispose();
            _vectorIndex = _vectorIndexFactory.Load(_config.FaissIndexPath);
        }
    }

    /// <summary>
    /// Returns the number of vectors currently stored in FAISS so callers can report ingestion status.
    /// </summary>
    public long GetIndexSize()
    {
        return _vectorIndex?.Count ?? 0;
    }

    /// <summary>
    /// Releases Lucene and FAISS resources once ingestion has completed.
    /// </summary>
    public void Dispose()
    {
        _luceneWriter?.Dispose();
        _luceneDirectory?.Dispose();
        _vectorIndex?.Dispose();
    }

    /// <summary>
    /// Normalises the configured Lucene path so relative locations resolve against the current working directory.
    /// </summary>
    private string ResolveLucenePath()
    {
        var lucenePath = _config.LuceneIndexPath;
        if (string.IsNullOrWhiteSpace(lucenePath))
        {
            lucenePath = "lucene_index";
        }

        return System.IO.Path.IsPathRooted(lucenePath)
            ? lucenePath
            : System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), lucenePath);
    }
}
