using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Core.Models;
using PyRagix.Net.Ingestion;
using PyRagix.Net.Retrieval;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests.Integration;

/// <summary>
/// Exercises the ingestion + hybrid retrieval path end-to-end using the in-memory index.
/// Validates that chunks written via IndexService can be read back through HybridRetriever.
/// </summary>
public class HybridRetrieverIntegrationTests
{
    [Fact]
    public async Task SearchAsync_WithVectorIndex_ReturnsMatchingChunk()
    {
        using var tempDir = new TempDirectory();
        var config = new PyRagixConfig
        {
            EmbeddingDimension = 4,
            DatabasePath = tempDir.Resolve("pyragix.db"),
            FaissIndexPath = tempDir.Resolve("faiss_index.bin"),
            LuceneIndexPath = tempDir.Resolve("lucene_index"),
            EnableHybridSearch = false
        };

        // Seed index with two simple chunks.
        var vectorFactory = new InMemoryVectorIndexFactory();

        using (var dbContext = new PyRagixDbContext(config.DatabasePath))
        {
            dbContext.EnsureCreated();

            using var indexService = new IndexService(config, dbContext, vectorFactory);
            var chunkBatch = new List<(string content, float[] embedding, ChunkMetadata metadata)>
            {
                (
                    "Alpha chunk about vectors",
                    new float[] { 1f, 0f, 0f, 0f },
                    new ChunkMetadata
                    {
                        Content = "Alpha chunk about vectors",
                        SourceFile = "alpha.txt",
                        FileType = "txt",
                        ChunkIndex = 0,
                        TotalChunks = 1
                    }
                ),
                (
                    "Beta chunk about keywords",
                    new float[] { 0f, 1f, 0f, 0f },
                    new ChunkMetadata
                    {
                        Content = "Beta chunk about keywords",
                        SourceFile = "beta.txt",
                        FileType = "txt",
                        ChunkIndex = 0,
                        TotalChunks = 1
                    }
                )
            };

            await indexService.AddChunksAsync(chunkBatch);
            indexService.SaveFaissIndex();
        }

        await using var retrievalContext = new PyRagixDbContext(config.DatabasePath);
        using var retriever = new HybridRetriever(config, retrievalContext, vectorFactory);

        var results = await retriever.SearchAsync(
            new float[] { 1f, 0f, 0f, 0f },
            "Alpha chunk about vectors",
            topK: 1);

        var chunk = Assert.Single(results);
        Assert.Equal("Alpha chunk about vectors", chunk.Content);
        Assert.Equal("alpha.txt", chunk.SourceFile);
    }
}
