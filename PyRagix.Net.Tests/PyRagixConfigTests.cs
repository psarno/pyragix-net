using System;
using System.IO;
using PyRagix.Net.Config;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Validates that <see cref="PyRagixConfig"/> loads sensible defaults and enforces guard rails on user-provided values.
/// </summary>
public class PyRagixConfigTests
{
    [Fact]
    public void LoadFromToml_WhenFileMissing_ReturnsDefaults()
    {
        var fileName = $"{Guid.NewGuid():N}.toml";

        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }

        // When the file is missing we expect the in-memory defaults to be used so the engine can still boot.
        var config = PyRagixConfig.LoadFromToml(fileName);

        Assert.Equal("./Models/embeddings/model.onnx", config.EmbeddingModelPath);
        Assert.Equal(384, config.EmbeddingDimension);
        Assert.True(config.EnableSemanticChunking);
    }

    [Fact]
    public void LoadFromToml_WithCustomValues_BindsProperties()
    {
        var fileName = $"{Guid.NewGuid():N}.toml";

        try
        {
            var toml = """
                ChunkSize = 1024
                ChunkOverlap = 128
                EnableSemanticChunking = false
                HybridAlpha = 0.3
                DefaultTopK = 5
                """;

            File.WriteAllText(fileName, toml);

            var config = PyRagixConfig.LoadFromToml(fileName);

            // Every overridden value should flow back into the strongly typed object.
            Assert.Equal(1024, config.ChunkSize);
            Assert.Equal(128, config.ChunkOverlap);
            Assert.False(config.EnableSemanticChunking);
            Assert.Equal(0.3, config.HybridAlpha, 3);
            Assert.Equal(5, config.DefaultTopK);
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    [Fact]
    public void Validate_WhenChunkOverlapIsTooLarge_Throws()
    {
        var config = new PyRagixConfig
        {
            ChunkSize = 200,
            ChunkOverlap = 200,
        };

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_WhenHybridAlphaIsOutsideBounds_Throws()
    {
        var config = new PyRagixConfig
        {
            HybridAlpha = 1.5,
        };

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_WhenTopKIsInvalid_Throws()
    {
        var config = new PyRagixConfig
        {
            DefaultTopK = 0,
        };

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_WhenQueryExpansionCountIsInvalid_Throws()
    {
        var config = new PyRagixConfig
        {
            QueryExpansionCount = 0,
        };

        // The pipeline requires at least one query variant to operate; zero should be rejected.
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void TryValidateResources_Ingestion_WhenEmbeddingMissing_ReturnsErrors()
    {
        var config = new PyRagixConfig
        {
            EmbeddingModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "model.onnx")
        };

        var result = config.TryValidateResources(ResourceValidationMode.Ingestion, out var errors);

        Assert.False(result);
        Assert.Contains(errors, e => e.Contains("Embedding model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryValidateResources_Retrieval_WhenAllAssetsExist_Succeeds()
    {
        using var tempDir = new TempDirectory();
        var embeddingPath = tempDir.Resolve("embedding.onnx");
        var rerankerPath = tempDir.Resolve("reranker.onnx");
        var faissPath = tempDir.Resolve("faiss_index.bin");
        var dbPath = tempDir.Resolve("pyragix.db");

        File.WriteAllText(embeddingPath, "test");
        File.WriteAllText(rerankerPath, "test");
        File.WriteAllText(faissPath, "test");
        File.WriteAllText(dbPath, "test");

        var config = new PyRagixConfig
        {
            EmbeddingModelPath = embeddingPath,
            RerankerModelPath = rerankerPath,
            FaissIndexPath = faissPath,
            DatabasePath = dbPath
        };

        var result = config.TryValidateResources(ResourceValidationMode.Retrieval, out var errors);

        Assert.True(result);
        Assert.Empty(errors);
    }
}
