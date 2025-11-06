using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Polly.Retry;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Resilience;
using System.Text;
using System.Text.RegularExpressions;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Generates sentence embeddings using an ONNX Runtime session, mirroring the Python MiniLM flow.
/// </summary>
public class EmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly PyRagixConfig _config;
    private readonly int _maxTokens = 512; // Default for MiniLM models
    private readonly AsyncRetryPolicy _inferenceRetryPolicy;

    /// <summary>
    /// Creates the ONNX session once so repeated batches reuse the same native resources.
    /// </summary>
    public EmbeddingService(PyRagixConfig config)
    {
        _config = config;
        _inferenceRetryPolicy = RetryPolicies.CreateAsyncPolicy("ONNX embedding inference");

        // Configure session options for GPU if enabled
        var sessionOptions = new SessionOptions();
        if (config.GpuEnabled)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA(config.GpuDeviceId);
            }
            catch
            {
                Console.WriteLine("CUDA not available, falling back to CPU");
            }
        }

        if (!File.Exists(config.EmbeddingModelPath))
        {
            throw new FileNotFoundException($"Embedding model not found: {config.EmbeddingModelPath}");
        }

        _session = new InferenceSession(config.EmbeddingModelPath, sessionOptions);
    }

    /// <summary>
    /// Convenience helper that embeds a single string by delegating to the batched API.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text)
    {
        var embeddings = await EmbedBatchAsync(new[] { text });
        return embeddings[0];
    }

    /// <summary>
    /// Streams the input through the ONNX session in batches, respecting the configured batch size.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();

        // Process in batches so we do not exhaust VRAM/host memory on large queries.
        for (int i = 0; i < textList.Count; i += _config.EmbeddingBatchSize)
        {
            var batch = textList.Skip(i).Take(_config.EmbeddingBatchSize).ToList();
            var batchResults = await ProcessBatchAsync(batch);
            results.AddRange(batchResults);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Runs the ONNX model for the supplied batch on a background thread pool thread.
    /// </summary>
    private async Task<List<float[]>> ProcessBatchAsync(List<string> batch)
    {
        return await _inferenceRetryPolicy.ExecuteAsync(() => Task.Run(() =>
        {
            var results = new List<float[]>();

            foreach (var text in batch)
            {
                // Simple tokenization (word-piece approximation)
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
                using var output = _session.Run(inputs);
                var embedding = output.First().AsTensor<float>();

                // Mean pooling (average across sequence dimension)
                var vector = MeanPooling(embedding, tokens.Length);
                results.Add(vector);
            }

            return results;
        }));
    }

    /// <summary>
    /// Extremely lightweight tokenizer that mirrors the Python placeholder implementation.
    /// Replace with a proper vocabulary-backed tokenizer when parity is required.
    /// </summary>
    private long[] Tokenize(string text)
    {
        // Lowercase and clean
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^\w\s]", " ");

        // Split into tokens
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        // Simple hash-based vocabulary (maps to 0-30000 range)
        // This is a SIMPLIFIED placeholder - real implementation should use model's vocab
        var tokens = words.Take(_maxTokens - 2)
            .Select(w => (long)(Math.Abs(w.GetHashCode()) % 30000 + 1))
            .ToList();

        // Add CLS and SEP tokens
        tokens.Insert(0, 101); // [CLS]
        tokens.Add(102); // [SEP]

        // Pad to max length
        while (tokens.Count < _maxTokens)
        {
            tokens.Add(0); // [PAD]
        }

        return tokens.Take(_maxTokens).ToArray();
    }

    /// <summary>
    /// Applies mean pooling across the sequence dimension followed by L2 normalisation, matching sentence-transformers defaults.
    /// </summary>
    private float[] MeanPooling(Tensor<float> embeddings, int sequenceLength)
    {
        var hiddenSize = _config.EmbeddingDimension;
        var result = new float[hiddenSize];

        for (int i = 0; i < hiddenSize; i++)
        {
            float sum = 0;
            for (int j = 0; j < sequenceLength; j++)
            {
                sum += embeddings[0, j, i];
            }
            result[i] = sum / sequenceLength;
        }

        // L2 normalization
        var norm = Math.Sqrt(result.Sum(x => x * x));
        for (int i = 0; i < hiddenSize; i++)
        {
            result[i] /= (float)norm;
        }

        return result;
    }

    /// <summary>
    /// Disposes the underlying ONNX session and associated native resources.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
