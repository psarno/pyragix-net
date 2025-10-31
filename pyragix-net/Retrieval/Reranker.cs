using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Cross-encoder reranker that scores query/chunk pairs to improve precision in the final result set.
/// Uses the same placeholder tokenisation strategy as the Python port until a shared tokenizer is introduced.
/// </summary>
public class Reranker : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly InferenceSession? _session;

    /// <summary>
    /// Initialises the ONNX session if reranking is enabled and the model file is present.
    /// </summary>
    public Reranker(PyRagixConfig config)
    {
        _config = config;

        if (!config.EnableReranking)
        {
            return;
        }

        if (!File.Exists(config.RerankerModelPath))
        {
            Console.WriteLine($"Warning: Reranker model not found at {config.RerankerModelPath}. Reranking disabled.");
            return;
        }

        var sessionOptions = new SessionOptions();
        if (config.GpuEnabled)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA(config.GpuDeviceId);
            }
            catch
            {
                // Fall back to CPU
            }
        }

        _session = new InferenceSession(config.RerankerModelPath, sessionOptions);
    }

    /// <summary>
    /// Scores each chunk against the query and returns the top results ordered by relevance.
    /// </summary>
    public async Task<List<ChunkMetadata>> RerankAsync(string query, List<ChunkMetadata> chunks)
    {
        if (!_config.EnableReranking || _session == null || chunks.Count == 0)
        {
            return chunks;
        }

        return await Task.Run(() =>
        {
            var scores = new List<(ChunkMetadata chunk, float score)>();

            foreach (var chunk in chunks)
            {
                var score = ScorePair(query, chunk.Content);
                scores.Add((chunk, score));
            }

            // Sort by score descending
            return scores
                .OrderByDescending(s => s.score)
                .Select(s => s.chunk)
                .Take(_config.DefaultTopK)
                .ToList();
        });
    }

    /// <summary>
    /// Produces a single relevance score for the query/document pair by invoking the ONNX cross-encoder.
    /// </summary>
    private float ScorePair(string query, string document)
    {
        // Format: [CLS] query [SEP] document [SEP]
        var text = $"[CLS] {query} [SEP] {document} [SEP]";

        // Simple tokenization (same as embedding service)
        var tokens = Tokenize(text);

        // Create input tensors
        var inputIds = new DenseTensor<long>(new[] { 1, tokens.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.Length });

        for (int i = 0; i < tokens.Length; i++)
        {
            inputIds[0, i] = tokens[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        // Run inference
        using var output = _session!.Run(inputs);
        var logits = output.First().AsTensor<float>();

        // Return relevance score (typically at index [0,0])
        return logits[0, 0];
    }

    /// <summary>
    /// Placeholder tokenizer that mirrors <see cref="EmbeddingService"/>; replace with a shared tokenizer when available.
    /// </summary>
    private long[] Tokenize(string text)
    {
        text = text.ToLowerInvariant();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[^\w\s]", " ");

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var tokens = words.Take(510) // Leave room for CLS/SEP
            .Select(w => (long)(Math.Abs(w.GetHashCode()) % 30000 + 1))
            .ToList();

        tokens.Insert(0, 101); // [CLS]
        tokens.Add(102); // [SEP]

        while (tokens.Count < 512)
        {
            tokens.Add(0); // [PAD]
        }

        return tokens.Take(512).ToArray();
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
