using FaissNet;
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

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Hybrid search combining FAISS (semantic) + Lucene BM25 (keyword)
/// Uses Reciprocal Rank Fusion (RRF) to merge results
/// </summary>
public class HybridRetriever : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private FaissNet.Index? _faissIndex;
    private IndexReader? _luceneReader;
    private IndexSearcher? _luceneSearcher;
    private FSDirectory? _luceneDirectory;

    public HybridRetriever(PyRagixConfig config, PyRagixDbContext dbContext)
    {
        _config = config;
        _dbContext = dbContext;
        LoadIndexes();
    }

    private void LoadIndexes()
    {
        // Load FAISS index
        if (File.Exists(_config.FaissIndexPath))
        {
            _faissIndex = FaissNet.Index.Load(_config.FaissIndexPath);
        }

        // Load Lucene index
        var lucenePath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "lucene_index");
        if (System.IO.Directory.Exists(lucenePath))
        {
            _luceneDirectory = FSDirectory.Open(lucenePath);
            _luceneReader = DirectoryReader.Open(_luceneDirectory);
            _luceneSearcher = new IndexSearcher(_luceneReader);
        }
    }

    /// <summary>
    /// Hybrid search: FAISS + BM25 with RRF fusion
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
    /// FAISS vector search
    /// </summary>
    private async Task<List<ChunkMetadata>> VectorSearchAsync(float[] queryEmbedding, int k)
    {
        if (_faissIndex == null)
        {
            throw new InvalidOperationException("FAISS index not loaded");
        }

        return await Task.Run(async () =>
        {
            var (distances, indices) = _faissIndex.Search(new[] { queryEmbedding }, k);

            var chunks = new List<ChunkMetadata>();
            for (int i = 0; i < indices[0].Length; i++)
            {
                var idx = (int)indices[0][i];
                if (idx == -1) continue; // No result

                // FAISS index position = SQLite ID (assumes sequential ingestion)
                var chunk = await _dbContext.Chunks.FindAsync(idx + 1);
                if (chunk != null)
                {
                    chunks.Add(chunk);
                }
            }

            return chunks;
        });
    }

    /// <summary>
    /// Lucene BM25 keyword search
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
    /// Reciprocal Rank Fusion: weighted merge of two ranked lists
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

    public void Dispose()
    {
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        _faissIndex?.Dispose();
    }
}
