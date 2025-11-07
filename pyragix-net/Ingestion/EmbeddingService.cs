using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Polly.Retry;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Resilience;
using PyRagix.Net.Core.Tokenization;
using System.Linq;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Generates sentence embeddings using an ONNX Runtime session, mirroring the Python MiniLM flow.
/// </summary>
public class EmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly PyRagixConfig _config;
    private readonly AsyncRetryPolicy _inferenceRetryPolicy;
    private readonly BertTokenizer _tokenizer;

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

        var tokenizerDirectory = BertTokenizer.InferAssetsDirectory(config.EmbeddingModelPath);
        _tokenizer = new BertTokenizer(tokenizerDirectory);
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
                var encoding = _tokenizer.Encode(text);

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

                using var output = _session.Run(inputs);
                var embedding = output.First().AsTensor<float>();

                var vector = MeanPooling(embedding, encoding.AttentionMask);
                results.Add(vector);
            }

            return results;
        }));
    }

    /// <summary>
    /// Applies mean pooling across the sequence dimension followed by L2 normalisation, matching sentence-transformers defaults.
    /// </summary>
    private float[] MeanPooling(Tensor<float> embeddings, long[] attentionMask)
    {
        var hiddenSize = _config.EmbeddingDimension;
        var result = new float[hiddenSize];
        double maskSum = 0;

        for (int token = 0; token < attentionMask.Length; token++)
        {
            if (attentionMask[token] == 0)
            {
                continue;
            }

            maskSum += 1;

            for (int dim = 0; dim < hiddenSize; dim++)
            {
                result[dim] += embeddings[0, token, dim];
            }
        }

        if (maskSum == 0)
        {
            maskSum = 1;
        }

        for (int i = 0; i < hiddenSize; i++)
        {
            result[i] = (float)(result[i] / maskSum);
        }

        var norm = Math.Sqrt(result.Sum(x => x * x));
        if (norm == 0)
        {
            return result;
        }

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
