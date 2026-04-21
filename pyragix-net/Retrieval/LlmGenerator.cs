using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;
using System.Text;
using System.Text.Json;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Wraps calls to an OpenAI-compatible LLM server for grounded answer generation.
/// Works with llamacpp, KoboldCpp, LM Studio, vLLM, LocalAI, Ollama (/v1), and any other server
/// that implements the <c>/v1/chat/completions</c> endpoint.
/// </summary>
public class LlmGenerator
{
    private readonly PyRagixConfig _config;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Configures a dedicated <see cref="HttpClient"/> with the project-level timeout.
    /// </summary>
    public LlmGenerator(PyRagixConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.RequestTimeout)
        };
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
            var response = await CallLlmAsync(prompt);
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
    /// Calls the OpenAI-compatible <c>/v1/chat/completions</c> endpoint and returns the response text.
    /// </summary>
    private async Task<string> CallLlmAsync(string prompt)
    {
        var requestBody = new
        {
            model = _config.LlmModel,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = _config.Temperature,
            top_p = _config.TopP,
            max_tokens = _config.MaxTokens,
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

    /// <summary>
    /// Performs a lightweight health check against the <c>/v1/models</c> endpoint,
    /// which is supported by all major OpenAI-compatible inference servers.
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.LlmEndpoint}/v1/models");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
