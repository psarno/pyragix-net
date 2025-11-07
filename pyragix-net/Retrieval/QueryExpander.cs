using Polly.Retry;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Resilience;
using System.Text.Json;
using System.Text;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Calls an Ollama model to generate alternative phrasings of the user's question, boosting recall for lexical/semantic search.
/// </summary>
public class QueryExpander
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>
    /// Initialises the expander with a dedicated <see cref="HttpClient"/> respecting the configured timeout.
    /// </summary>
    public QueryExpander(PyRagixConfig config, HttpClient? httpClient = null, AsyncRetryPolicy? httpRetryPolicy = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
        if (httpClient == null)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(config.RequestTimeout);
        }

        _httpRetryPolicy = httpRetryPolicy ?? RetryPolicies.CreateHttpPolicy("Ollama query expansion");
    }

    /// <summary>
    /// Generates additional, distinct question variants (when enabled) to broaden the retrieval surface area.
    /// </summary>
    public async Task<List<string>> ExpandQueryAsync(string originalQuery)
    {
        if (!_config.EnableQueryExpansion)
        {
            return new List<string> { originalQuery };
        }

        var variants = new List<string> { originalQuery };

        var prompt = $@"Generate {_config.QueryExpansionCount} alternative phrasings of this question.
Return ONLY the alternative questions, one per line, without numbering or explanations.

Original question: {originalQuery}

Alternative questions:";

        try
        {
            var response = await CallOllamaAsync(prompt);

            // Parse response into separate queries
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && l.Contains('?'))
                .Take(_config.QueryExpansionCount)
                .ToList();

            variants.AddRange(lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Query expansion failed: {ex.Message}. Using original query only.");
        }

        // Deduplicate to guard against the model echoing the original question or repeating a variant verbatim.
        return variants.Distinct().ToList();
    }

    /// <summary>
    /// Invokes the Ollama generate endpoint and returns the raw response text.
    /// </summary>
    private async Task<string> CallOllamaAsync(string prompt)
    {
        var requestBody = new
        {
            model = _config.OllamaModel,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = _config.Temperature,
                top_p = _config.TopP
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await _httpRetryPolicy.ExecuteAsync(async () =>
        {
            var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint}/api/generate", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
        });
    }
}
