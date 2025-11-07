using Polly.Retry;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;
using System.Text;
using System.Text.Json;
using PyRagix.Net.Core.Resilience;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Wraps calls to Ollama for grounded answer generation.
/// This mirrors the Python generator so prompt formatting stays aligned across languages.
/// </summary>
public class OllamaGenerator
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>
    /// Configures a dedicated <see cref="HttpClient"/> with the project-level timeout.
    /// </summary>
    public OllamaGenerator(PyRagixConfig config, HttpClient? httpClient = null, AsyncRetryPolicy? httpRetryPolicy = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
        if (httpClient == null)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(config.RequestTimeout);
        }

        _httpRetryPolicy = httpRetryPolicy ?? RetryPolicies.CreateHttpPolicy("Ollama answer generation");
    }

    /// <summary>
    /// Generates an answer conditioned on the supplied context chunks.
    /// Falls back to a descriptive error message when the call fails.
    /// </summary>
    public async Task<string> GenerateAnswerAsync(string question, List<ChunkMetadata> context)
    {
        var contextText = FormatContext(context);
        var prompt = BuildPrompt(question, contextText);

        try
        {
            var response = await CallOllamaAsync(prompt);
            return response;
        }
        catch (Exception ex)
        {
            return $"Error generating answer: {ex.Message}";
        }
    }

    /// <summary>
    /// Formats each chunk with a numbered header to make citations easier for the LLM.
    /// </summary>
    private string FormatContext(List<ChunkMetadata> chunks)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"[Document {i + 1}]");
            sb.AppendLine(chunks[i].Content);
            sb.AppendLine($"Source: {Path.GetFileName(chunks[i].SourceFile)}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the instruction prompt that constrains the model to grounded answers.
    /// </summary>
    private string BuildPrompt(string question, string context)
    {
        return $@"You are a helpful assistant that answers questions based on the provided context documents.

Context:
{context}

Question: {question}

Instructions:
- Answer the question using ONLY information from the context above
- If the context doesn't contain enough information, say so
- Be concise and direct
- Cite which document number you're using if helpful

Answer:";
    }

    /// <summary>
    /// Calls the Ollama generate endpoint and returns the raw response text.
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
                top_p = _config.TopP,
                num_predict = _config.MaxTokens
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

    /// <summary>
    /// Performs a lightweight health check against the Ollama tags endpoint to confirm availability.
    /// </summary>
    public async Task<bool> IsOllamaAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.OllamaEndpoint}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
