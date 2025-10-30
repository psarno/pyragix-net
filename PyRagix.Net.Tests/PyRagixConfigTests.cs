using PyRagix.Net.Config;
using Xunit;

namespace PyRagix.Net.Tests;

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

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }
}
