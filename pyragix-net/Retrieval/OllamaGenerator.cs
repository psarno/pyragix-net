using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;
using System.Text;
using System.Text.Json;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Ollama LLM client for answer generation
/// </summary>
public class OllamaGenerator
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;

    public OllamaGenerator(PyRagixConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.RequestTimeout)
        };
    }

    /// <summary>
    /// Generate answer based on retrieved context chunks
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
    /// Format context chunks for prompt
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
    /// Build RAG prompt
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
    /// Call Ollama API
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

        var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint}/api/generate", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Check if Ollama is running
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
