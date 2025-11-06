using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Polly;
using PyRagix.Net.Config;
using PyRagix.Net.Retrieval;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Covers resilience scenarios for <see cref="QueryExpander"/> when the Ollama endpoint times out.
/// </summary>
public class QueryExpanderTests
{
    [Fact]
    public async Task ExpandQueryAsync_OnInitialTimeout_RetriesAndReturnsExpandedVariants()
    {
        var config = new PyRagixConfig
        {
            EnableQueryExpansion = true,
            QueryExpansionCount = 2,
            RequestTimeout = 5,
            OllamaEndpoint = "http://fake.local"
        };

        var expander = new QueryExpander(config);

        var handler = new TimeoutThenSuccessHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(config.RequestTimeout)
        };

        SetPrivateField(expander, "_httpClient", httpClient);
        SetPrivateField(expander, "_httpRetryPolicy", Policy.Handle<TaskCanceledException>().RetryAsync(1));

        var variants = await expander.ExpandQueryAsync("How are you?");

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("Variant question?", variants);
        Assert.Contains("Follow-up question?", variants);
        Assert.Contains("How are you?", variants);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private sealed class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (CallCount == 1)
            {
                throw new TaskCanceledException("Simulated timeout.");
            }

            var payload = "{\"response\":\"Variant question?\\nFollow-up question?\"}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
