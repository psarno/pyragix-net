using System.IO;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;
using PyRagix.Net.Ingestion.Vector;
using PyRagix.Net.Retrieval;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Ensures <see cref="HybridRetriever"/> retries FAISS operations when the vector index throws transient errors.
/// </summary>
public class HybridRetrieverResilienceTests
{
    [Fact]
    public async Task SearchAsync_WhenVectorSearchFailsInitially_RetriesAndReturnsChunk()
    {
        using var tempDir = new TempDirectory();
        var dbPath = tempDir.Resolve("pyragix.db");
        var faissPath = tempDir.Resolve("faiss_index.bin");
        File.WriteAllText(faissPath, "placeholder");

        var config = new PyRagixConfig
        {
            DatabasePath = dbPath,
            FaissIndexPath = faissPath,
            LuceneIndexPath = tempDir.Resolve("lucene_index"),
            EnableHybridSearch = false
        };

        int chunkId;
        using (var dbContext = new PyRagixDbContext(dbPath))
        {
            dbContext.EnsureCreated();
            var newChunk = new ChunkMetadata
            {
                Content = "Resilient chunk",
                SourceFile = "resilient.txt",
                FileType = "txt",
                ChunkIndex = 0,
                TotalChunks = 1
            };

            dbContext.Chunks.Add(newChunk);
            dbContext.SaveChanges();
            chunkId = newChunk.Id;
        }

        var vectorIndex = new FailingOnceVectorIndex(chunkId);
        var factory = new StubVectorIndexFactory(vectorIndex);

        await using var retrievalContext = new PyRagixDbContext(dbPath);
        using var retriever = new HybridRetriever(config, retrievalContext, factory);

        var results = await retriever.SearchAsync(new float[] { 1f }, "resilient query", 1);

        Assert.Equal(2, vectorIndex.SearchCallCount);
        var chunk = Assert.Single(results);
        Assert.Equal(chunkId, chunk.Id);
    }

    private sealed class FailingOnceVectorIndex : IVectorIndex
    {
        private readonly int _chunkId;
        private bool _hasThrown;

        public FailingOnceVectorIndex(int chunkId)
        {
            _chunkId = chunkId;
        }

        public int SearchCallCount { get; private set; }

        public (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK)
        {
            SearchCallCount++;

            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new IOException("Simulated FAISS failure.");
            }

            var distances = new[] { new[] { 1f } };
            var indices = new[] { new long[] { _chunkId } };
            return (distances, indices);
        }

        public void AddWithIds(float[][] vectors, long[] ids)
        {
        }

        public void Save(string path)
        {
        }

        public long Count => 1;

        public void Dispose()
        {
        }
    }

    private sealed class StubVectorIndexFactory : IVectorIndexFactory
    {
        private readonly IVectorIndex _index;

        public StubVectorIndexFactory(IVectorIndex index)
        {
            _index = index;
        }

        public IVectorIndex Create(int dimension) => _index;

        public IVectorIndex Load(string path) => _index;
    }
}
