using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;
using Microsoft.EntityFrameworkCore;
using PyRagix.Net.Ingestion.Vector;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Executes hybrid retrieval by combining FAISS vector lookup with Lucene BM25 keyword search.
/// Results from both sources are merged via Reciprocal Rank Fusion, matching the Python approach.
/// </summary>
public class HybridRetriever : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private readonly IVectorIndexFactory _vectorIndexFactory;
    private IVectorIndex? _vectorIndex;
    private IndexReader? _luceneReader;
    private IndexSearcher? _luceneSearcher;
    private FSDirectory? _luceneDirectory;

    /// <summary>
    /// Loads both FAISS and Lucene indexes so queries can be served immediately after construction.
    /// </summary>
    public HybridRetriever(PyRagixConfig config, PyRagixDbContext dbContext, IVectorIndexFactory? vectorIndexFactory = null)
    {
        _config = config;
        _dbContext = dbContext;
        _vectorIndexFactory = vectorIndexFactory ?? VectorIndexFactoryResolver.GetDefault();
        LoadIndexes();
    }

    /// <summary>
    /// Opens the FAISS and Lucene indexes from disk if the corresponding files exist.
    /// </summary>
    private void LoadIndexes()
    {
        // Load FAISS index
        if (File.Exists(_config.FaissIndexPath))
        {
            try
            {
                _vectorIndex?.Dispose();
                _vectorIndex = _vectorIndexFactory.Load(_config.FaissIndexPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Vector index at '{_config.FaissIndexPath}' could not be loaded ({ex.Message}). Delete the file and re-run ingestion.");
                _vectorIndex = null;
            }
        }

        // Load Lucene index so keyword queries can be executed without re-ingesting documents.
        var lucenePath = ResolveLucenePath();
        if (System.IO.Directory.Exists(lucenePath))
        {
            _luceneDirectory = FSDirectory.Open(lucenePath);
            _luceneReader = DirectoryReader.Open(_luceneDirectory);
            _luceneSearcher = new IndexSearcher(_luceneReader);
        }
    }

    /// <summary>
    /// Performs the configured hybrid search.
    /// Falls back to pure vector search when hybrid search is disabled.
    /// </summary>
    public async Task<List<ChunkMetadata>> SearchAsync(float[] queryEmbedding, string queryText, int topK)
    {
        if (!_config.EnableHybridSearch)
        {
            // Vector-only search
            return await VectorSearchAsync(queryEmbedding, topK);
        }

        // Get results from both indexes
        var vectorResults = await VectorSearchAsync(queryEmbedding, topK * 2);
        var keywordResults = await KeywordSearchAsync(queryText, topK * 2);

        // Reciprocal Rank Fusion
        var fusedResults = FuseResults(vectorResults, keywordResults, topK);

        return fusedResults;
    }

    /// <summary>
    /// Queries FAISS and materialises the associated chunk metadata records from SQLite.
    /// </summary>
    private async Task<List<ChunkMetadata>> VectorSearchAsync(float[] queryEmbedding, int k)
    {
        if (_vectorIndex == null)
        {
            throw new InvalidOperationException("Vector index not loaded");
        }

        return await Task.Run(async () =>
        {
            var (_, indices) = _vectorIndex.Search(new[] { queryEmbedding }, k);

            var chunks = new List<ChunkMetadata>();
            for (int i = 0; i < indices[0].Length; i++)
            {
                var id = (int)indices[0][i];
                if (id == -1) continue; // No result
                // Use FindAsync so EF can leverage the context cache when multiple variants hit the same chunk.
                var chunk = await _dbContext.Chunks.FindAsync(id);
                if (chunk != null)
                {
                    chunks.Add(chunk);
                }
            }

            return chunks;
        });
    }

    /// <summary>
    /// Executes a BM25 search against the Lucene index and fetches the matching chunk metadata records.
    /// </summary>
    private async Task<List<ChunkMetadata>> KeywordSearchAsync(string queryText, int k)
    {
        if (_luceneSearcher == null)
        {
            return new List<ChunkMetadata>();
        }

        return await Task.Run(async () =>
        {
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            var query = parser.Parse(queryText);

            var hits = _luceneSearcher.Search(query, k);

            var chunks = new List<ChunkMetadata>();
            foreach (var hit in hits.ScoreDocs)
            {
                var doc = _luceneSearcher.Doc(hit.Doc);
                var chunkId = doc.GetField("chunk_id")?.GetInt32Value();

                if (chunkId.HasValue)
                {
                    var chunk = await _dbContext.Chunks.FindAsync(chunkId.Value);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                    }
                }
            }

            return chunks;
        });
    }

    /// <summary>
    /// Applies Reciprocal Rank Fusion to blend semantic and keyword result lists into a single ranking.
    /// </summary>
    private List<ChunkMetadata> FuseResults(
        List<ChunkMetadata> vectorResults,
        List<ChunkMetadata> keywordResults,
        int topK)
    {
        var k = 60; // RRF constant
        var scores = new Dictionary<int, double>();

        // Score vector results
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var chunkId = vectorResults[i].Id;
            var score = _config.HybridAlpha / (k + i + 1);
            scores[chunkId] = scores.GetValueOrDefault(chunkId, 0) + score;
        }

        // Score keyword results
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var chunkId = keywordResults[i].Id;
            var score = (1 - _config.HybridAlpha) / (k + i + 1);
            scores[chunkId] = scores.GetValueOrDefault(chunkId, 0) + score;
        }

        // Merge and sort by fused score
        var allChunks = vectorResults.Concat(keywordResults)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        return allChunks
            .OrderByDescending(c => scores.GetValueOrDefault(c.Id, 0))
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Releases Lucene and FAISS resources when the retriever is disposed.
    /// </summary>
    public void Dispose()
    {
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        _vectorIndex?.Dispose();
    }

    /// <summary>
    /// Resolves the configured Lucene directory to an absolute path.
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
