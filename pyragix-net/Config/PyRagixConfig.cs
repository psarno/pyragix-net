using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomlyn.Extensions.Configuration;

namespace PyRagix.Net.Config;

/// <summary>
/// Centralised configuration backing the ingestion and retrieval pipelines.
/// Values are typically sourced from <c>settings.toml</c> and mirrored from the Python project.
/// </summary>
public class PyRagixConfig
{
    // Paths
    public string EmbeddingModelPath { get; set; } = "./Models/embeddings/model.onnx";
    public string RerankerModelPath { get; set; } = "./Models/reranker/model.onnx";
    public string DatabasePath { get; set; } = "pyragix.db";
    public string FaissIndexPath { get; set; } = "faiss_index.bin";
    public string BM25IndexPath { get; set; } = "bm25_index.pkl";
    public string LuceneIndexPath { get; set; } = "lucene_index";

    // Ollama LLM
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen2.5:7b";
    public double Temperature { get; set; } = 0.1;
    public double TopP { get; set; } = 0.9;
    public int MaxTokens { get; set; } = 500;
    public int RequestTimeout { get; set; } = 180;

    // Chunking
    public bool EnableSemanticChunking { get; set; } = true;
    public int ChunkSize { get; set; } = 1600;
    public int ChunkOverlap { get; set; } = 200;

    // Embeddings
    public int EmbeddingBatchSize { get; set; } = 16;
    public int EmbeddingDimension { get; set; } = 384; // MiniLM-L6-v2

    // Query Expansion
    public bool EnableQueryExpansion { get; set; } = true;
    public int QueryExpansionCount { get; set; } = 3;

    // Hybrid Search
    public bool EnableHybridSearch { get; set; } = true;
    public double HybridAlpha { get; set; } = 0.7; // 70% semantic, 30% keyword

    // Reranking
    public bool EnableReranking { get; set; } = true;
    public int RerankTopK { get; set; } = 20;

    // Retrieval
    public int DefaultTopK { get; set; } = 7;

    // GPU
    public bool GpuEnabled { get; set; } = false;
    public int GpuDeviceId { get; set; } = 0;

    // OCR
    public int OcrBaseDpi { get; set; } = 150;
    public int OcrMaxDpi { get; set; } = 300;

    /// <summary>
    /// Hydrates a <see cref="PyRagixConfig"/> instance from the specified TOML file.
    /// Falls back to in-memory defaults when the file is absent so the engine can bootstrap fresh environments.
    /// </summary>
    public static PyRagixConfig LoadFromToml(string path = "settings.toml")
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddTomlFile(path, optional: true, reloadOnChange: false)
            .Build();

        var pyragixConfig = new PyRagixConfig();
        config.Bind(pyragixConfig);
        return pyragixConfig;
    }

    /// <summary>
    /// Asserts that the loaded values fall within the supported ranges for the pipeline components.
    /// </summary>
    public void Validate()
    {
        // Chunking must produce forward progress so downstream embedding batches have content to process.
        if (ChunkSize <= 0)
            throw new InvalidOperationException("ChunkSize must be greater than 0");

        // Overlap larger than the window would duplicate entire chunks and break sequential ingestion.
        if (ChunkOverlap >= ChunkSize)
            throw new InvalidOperationException("ChunkOverlap must be less than ChunkSize");

        // Reciprocal rank fusion relies on alpha being a proportion between semantic/keyword scorers.
        if (HybridAlpha < 0 || HybridAlpha > 1)
            throw new InvalidOperationException("HybridAlpha must be between 0 and 1");

        // Retrieval should always surface at least one chunk to the generator.
        if (DefaultTopK <= 0)
            throw new InvalidOperationException("DefaultTopK must be greater than 0");

        // Expansion count controls the number of extra embeddings to generate.
        if (QueryExpansionCount < 1)
            throw new InvalidOperationException("QueryExpansionCount must be at least 1");
    }

    /// <summary>
    /// Verifies that critical external assets exist before the engine starts work.
    /// Returns <c>false</c> and populates <paramref name="errors"/> when files are missing.
    /// </summary>
    public bool TryValidateResources(ResourceValidationMode mode, out List<string> errors)
    {
        errors = new List<string>();

        ValidateFileExists(EmbeddingModelPath, "Embedding model", errors);

        if (EnableReranking && mode == ResourceValidationMode.Retrieval)
        {
            ValidateFileExists(RerankerModelPath, "Reranker model", errors);
        }

        if (mode == ResourceValidationMode.Retrieval)
        {
            ValidateFileExists(FaissIndexPath, "FAISS index", errors);
            ValidateFileExists(DatabasePath, "Chunk metadata database", errors);
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Throws when <see cref="TryValidateResources"/> reports missing assets.
    /// </summary>
    public void ValidateResources(ResourceValidationMode mode)
    {
        if (TryValidateResources(mode, out var errors))
        {
            return;
        }

        var message = "Configuration bootstrap failed:" + Environment.NewLine +
                      string.Join(Environment.NewLine, errors.Select(e => $" - {e}"));
        throw new InvalidOperationException(message);
    }

    private void ValidateFileExists(string? path, string description, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{description} path is not configured.");
            return;
        }

        var resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
        {
            errors.Add($"{description} not found at '{resolvedPath}'.");
        }
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(Directory.GetCurrentDirectory(), path);
    }
}

/// <summary>
/// Indicates which pipeline stage is about to run so resource validation can require the appropriate assets.
/// </summary>
public enum ResourceValidationMode
{
    /// <summary>
    /// Ingestion requires ONNX models but does not depend on existing indexes.
    /// </summary>
    Ingestion,

    /// <summary>
    /// Retrieval requires previously generated indexes in addition to ONNX assets.
    /// </summary>
    Retrieval
}
