using PyRagix.Net.Config;
using PyRagix.Net.Ingestion;
using Xunit;

namespace PyRagix.Net.Tests;

public class SemanticChunkerTests
{
    [Fact]
    public void ChunkText_WithSemanticChunkingDisabled_UsesFixedWindow()
    {
        var config = new PyRagixConfig
        {
            EnableSemanticChunking = false,
            ChunkSize = 10,
            ChunkOverlap = 2,
        };

        var chunker = new SemanticChunker(config);
        var text = new string('A', 26);

        var chunks = chunker.ChunkText(text);

        Assert.Equal(4, chunks.Count);
        Assert.Equal(text.Substring(0, 10), chunks[0]);
        Assert.Equal(text.Substring(8, 10), chunks[1]);
        Assert.Equal(text.Substring(16, 10), chunks[2]);
        Assert.Equal(text.Substring(24, 2), chunks[3]);
    }

    [Fact]
    public void ChunkText_WithEmptyInput_ReturnsNoChunks()
    {
        var config = new PyRagixConfig
        {
            EnableSemanticChunking = true,
            ChunkSize = 100,
            ChunkOverlap = 20,
        };

        var chunker = new SemanticChunker(config);

        var chunks = chunker.ChunkText(string.Empty);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_WithSemanticChunkingEnabled_RespectsSentenceBoundaries()
    {
        var config = new PyRagixConfig
        {
            EnableSemanticChunking = true,
            ChunkSize = 50,
            ChunkOverlap = 10,
        };

        var chunker = new SemanticChunker(config);

        var text =
            "This is sentence one. This is sentence two, which is longer. Third sentence here.";

        var chunks = chunker.ChunkText(text);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("This is sentence one.", chunks[0]);
        Assert.Equal("This is sentence two, which is longer.", chunks[1]);
        Assert.Equal("Third sentence here.", chunks[2]);
    }
}
