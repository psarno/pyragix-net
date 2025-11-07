using System.Collections.Generic;
using System.Text.Json;
using Polly;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;
using PyRagix.Net.Retrieval;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Covers generator behaviour when no retrieval context is available.
/// </summary>
public class OllamaGeneratorTests
{
    [Fact]
    public async Task GenerateAnswerAsync_WithZeroChunks_StillCallsOllama()
    {
        var handler = new SequenceHttpMessageHandler(new[]
        {
            SequenceHttpMessageHandler.Respond("""{"response":"Mock answer"}""")
        });

        var httpClient = new HttpClient(handler);
        var retryPolicy = Policy.Handle<Exception>().RetryAsync(0);

        var config = new PyRagixConfig
        {
            OllamaEndpoint = "http://localhost:11434",
            OllamaModel = "mock"
        };

        var generator = new OllamaGenerator(config, httpClient, retryPolicy);

        var answer = await generator.GenerateAnswerAsync("What is RAG?", new List<ChunkMetadata>());

        Assert.Equal("Mock answer", answer);
        Assert.Equal(1, handler.CallCount);

        Assert.NotNull(handler.LastRequestBody);
        var payload = JsonDocument.Parse(handler.LastRequestBody!);
        var prompt = payload.RootElement.GetProperty("prompt").GetString();

        Assert.NotNull(prompt);
        var promptValue = prompt!;
        var hasBlankContext = promptValue.Contains("Context:\r\n\r\n", StringComparison.Ordinal) ||
                              promptValue.Contains("Context:\n\n", StringComparison.Ordinal);
        Assert.True(hasBlankContext, "Prompt should include an empty context block.");
        Assert.Contains("Question: What is RAG?", promptValue);
        Assert.DoesNotContain("[Document 1]", promptValue);
    }
}
