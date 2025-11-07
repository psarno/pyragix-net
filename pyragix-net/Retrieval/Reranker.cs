using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Models;
using PyRagix.Net.Core.Tokenization;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Cross-encoder reranker that scores query/chunk pairs to improve precision in the final result set.
/// Uses the same placeholder tokenisation strategy as the Python port until a shared tokenizer is introduced.
/// </summary>
public class Reranker : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly InferenceSession? _session;
    private readonly BertTokenizer? _tokenizer;

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

        var tokenizerDirectory = BertTokenizer.InferAssetsDirectory(config.RerankerModelPath);
        _tokenizer = new BertTokenizer(tokenizerDirectory);
    }

    /// <summary>
    /// Scores each chunk against the query and returns the top results ordered by relevance.
    /// </summary>
    public async Task<List<ChunkMetadata>> RerankAsync(string query, List<ChunkMetadata> chunks)
    {
        if (!_config.EnableReranking || _session == null || _tokenizer == null || chunks.Count == 0)
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
                .ToList();
        });
    }

    /// <summary>
    /// Produces a single relevance score for the query/document pair by invoking the ONNX cross-encoder.
    /// </summary>
    private float ScorePair(string query, string document)
    {
        var encoding = _tokenizer!.EncodePair(query, document);

        var inputIds = new DenseTensor<long>(new[] { 1, encoding.InputIds.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, encoding.AttentionMask.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, encoding.TokenTypeIds.Length });

        for (int i = 0; i < encoding.InputIds.Length; i++)
        {
            inputIds[0, i] = encoding.InputIds[i];
            attentionMask[0, i] = encoding.AttentionMask[i];
            tokenTypeIds[0, i] = encoding.TokenTypeIds[i];
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

    public void Dispose()
    {
        _session?.Dispose();
    }
}
