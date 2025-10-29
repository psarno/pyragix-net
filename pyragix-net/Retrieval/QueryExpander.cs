using PyRagix.Net.Config;
using System.Text.Json;
using System.Text;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Generates multiple query variants via Ollama for improved recall
/// </summary>
public class QueryExpander
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;

    public QueryExpander(PyRagixConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.RequestTimeout)
        };
    }

    /// <summary>
    /// Generate 3-5 query variants
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

        return variants.Distinct().ToList();
    }

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

        var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint}/api/generate", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }
}
