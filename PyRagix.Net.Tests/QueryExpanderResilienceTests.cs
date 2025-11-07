using Polly;
using PyRagix.Net.Config;
using PyRagix.Net.Retrieval;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Ensures the query expander retries transient Ollama failures (timeouts) before giving up.
/// </summary>
public class QueryExpanderResilienceTests
{
    [Fact]
    public async Task ExpandQueryAsync_WhenOllamaTimesOut_RetriesAndReturnsVariants()
    {
        var handler = new SequenceHttpMessageHandler(new[]
        {
            SequenceHttpMessageHandler.Throw(new TaskCanceledException("timeout 1")),
            SequenceHttpMessageHandler.Throw(new TaskCanceledException("timeout 2")),
            SequenceHttpMessageHandler.Respond("""{"response":"How does RAG work?\nWhat problem does retrieval solve?"}""")
        });

        var httpClient = new HttpClient(handler);
        var retryPolicy = Policy.Handle<Exception>().RetryAsync(2);

        var config = new PyRagixConfig
        {
            EnableQueryExpansion = true,
            QueryExpansionCount = 2,
            OllamaEndpoint = "http://localhost:11434",
            OllamaModel = "mock"
        };

        var expander = new QueryExpander(config, httpClient, retryPolicy);

        var variants = await expander.ExpandQueryAsync("What is RAG?");

        Assert.Equal(3, handler.CallCount);
        Assert.Contains("What is RAG?", variants);
        Assert.Contains("How does RAG work?", variants);
        Assert.Contains("What problem does retrieval solve?", variants);
    }
}
