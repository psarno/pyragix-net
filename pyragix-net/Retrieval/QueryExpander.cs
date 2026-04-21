using PyRagix.Net.Config;
using System.Text.Json;
using System.Text;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Calls an OpenAI-compatible LLM to generate alternative phrasings of the user's question, boosting recall for lexical/semantic search.
/// </summary>
public class QueryExpander
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes the expander with a dedicated <see cref="HttpClient"/> respecting the configured timeout.
    /// </summary>
    public QueryExpander(PyRagixConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.RequestTimeout)
        };
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
            var response = await CallLlmAsync(prompt);

            // Strip <think>...</think> blocks emitted by reasoning models (DeepSeek-R1, Qwen3, etc.)
            var thinkStart = response.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            var thinkEnd   = response.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkStart >= 0 && thinkEnd > thinkStart)
                response = response[(thinkEnd + "</think>".Length)..].Trim();

            // Parse response into separate queries
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                // Strip leading bullet/numbered list markers that thinking models frequently emit.
                .Select(StripListMarker)
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
    /// Removes leading bullet or numbered-list markers emitted by LLMs (e.g. <c>* </c>, <c>- </c>, <c>1. </c>).
    /// </summary>
    private static string StripListMarker(string line)
    {
        // Bullet markers: *, -, +
        if (line.Length > 1 && (line[0] == '*' || line[0] == '-' || line[0] == '+') && char.IsWhiteSpace(line[1]))
            return line[1..].TrimStart();

        // Numbered markers: "1. " or "1) "
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i > 0 && i < line.Length - 1 && (line[i] == '.' || line[i] == ')') && char.IsWhiteSpace(line[i + 1]))
            return line[(i + 1)..].TrimStart();

        return line;
    }

    /// <summary>
    /// Invokes the OpenAI-compatible <c>/v1/chat/completions</c> endpoint and returns the response text.
    /// </summary>
    private async Task<string> CallLlmAsync(string prompt)
    {
        var requestBody = new
        {
            model = _config.LlmModel,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = _config.Temperature,
            top_p = _config.TopP,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_config.LlmEndpoint}/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
