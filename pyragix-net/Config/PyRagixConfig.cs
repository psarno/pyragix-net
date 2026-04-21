using Microsoft.Extensions.Configuration;
using Tomlyn.Extensions.Configuration;

namespace PyRagix.Net.Config;

/// <summary>
/// Controls which ONNX Runtime execution provider is used for embedding and reranking inference.
/// Mirrors the auto-detection logic from the Python version's <c>faiss_manager.py</c>.
/// </summary>
public enum OnnxExecutionProvider
{
    /// <summary>Try CUDA first; silently falls back to CPU when CUDA is unavailable.</summary>
    Auto,
    /// <summary>Require CUDA. Throws <see cref="Exception"/> at startup if CUDA is not available.</summary>
    Cuda,
    /// <summary>CPU only. Safe on any machine; the default.</summary>
    Cpu
}

/// <summary>
/// Centralised configuration backing the ingestion and retrieval pipelines.
/// Values are typically sourced from <c>settings.toml</c> and mirrored from the Python project.
/// </summary>
public class PyRagixConfig
{
    // Paths
    /// <summary>Path to the ONNX embedding model file (e.g. <c>model.onnx</c> exported from MiniLM-L6-v2).</summary>
    public string EmbeddingModelPath { get; set; } = "./Models/embeddings/model.onnx";
    /// <summary>Path to the ONNX cross-encoder reranker model file.</summary>
    public string RerankerModelPath { get; set; } = "./Models/reranker/model.onnx";
    /// <summary>Path to the SQLite database file that stores chunk metadata.</summary>
    public string DatabasePath { get; set; } = "pyragix.db";
    /// <summary>Path where the FAISS vector index is persisted between sessions.</summary>
    public string FaissIndexPath { get; set; } = "faiss_index.bin";
    /// <summary>Path for the BM25 index artifact (reserved for future parity with the Python implementation).</summary>
    public string BM25IndexPath { get; set; } = "bm25_index.pkl";
    /// <summary>Directory path for the Lucene BM25 keyword index.</summary>
    public string LuceneIndexPath { get; set; } = "lucene_index";

    // LLM (any OpenAI-compatible server: llamacpp, KoboldCpp, Ollama, LM Studio, vLLM, LocalAI, etc.)
    /// <summary>Base URL of the OpenAI-compatible inference server (e.g. <c>http://localhost:8080</c> for llamacpp, <c>http://localhost:11434</c> for Ollama).</summary>
    public string LlmEndpoint { get; set; } = "http://localhost:8080";
    /// <summary>Model identifier sent in chat-completion requests; must match a model loaded by the inference server.</summary>
    public string LlmModel { get; set; } = "qwen2.5:7b";
    /// <summary>Sampling temperature for LLM generation; lower values produce more deterministic output.</summary>
    public double Temperature { get; set; } = 0.1;
    /// <summary>Nucleus sampling probability mass; only tokens within the top-p cumulative probability are considered.</summary>
    public double TopP { get; set; } = 0.9;
    /// <summary>Maximum number of tokens the LLM may generate per response.</summary>
    public int MaxTokens { get; set; } = 500;
    /// <summary>HTTP request timeout in seconds applied to all LLM calls.</summary>
    public int RequestTimeout { get; set; } = 180;

    // Chunking
    /// <summary>When <see langword="true"/>, splits documents at sentence boundaries; when <see langword="false"/>, uses fixed-size windows.</summary>
    public bool EnableSemanticChunking { get; set; } = true;
    /// <summary>Target character count for each chunk; the chunker will not exceed this before emitting a new chunk.</summary>
    public int ChunkSize { get; set; } = 1600;
    /// <summary>Number of characters carried forward from the previous chunk to maintain context continuity.</summary>
    public int ChunkOverlap { get; set; } = 200;

    // Embeddings
    /// <summary>Number of texts processed per ONNX inference call; higher values improve throughput at the cost of memory.</summary>
    public int EmbeddingBatchSize { get; set; } = 16;
    /// <summary>Dimensionality of the embedding vectors produced by the model (384 for MiniLM-L6-v2).</summary>
    public int EmbeddingDimension { get; set; } = 384; // MiniLM-L6-v2

    // Query Expansion
    /// <summary>When <see langword="true"/>, the LLM generates additional query variants to improve recall.</summary>
    public bool EnableQueryExpansion { get; set; } = true;
    /// <summary>Number of alternative query phrasings to generate in addition to the original.</summary>
    public int QueryExpansionCount { get; set; } = 3;

    // Hybrid Search
    /// <summary>When <see langword="true"/>, combines FAISS vector search with Lucene BM25 via Reciprocal Rank Fusion.</summary>
    public bool EnableHybridSearch { get; set; } = true;
    /// <summary>Weight given to vector search scores during fusion; <c>1 - HybridAlpha</c> goes to keyword search.</summary>
    public double HybridAlpha { get; set; } = 0.7; // 70% semantic, 30% keyword

    // Reranking
    /// <summary>When <see langword="true"/>, a cross-encoder ONNX model re-scores the initial candidate set.</summary>
    public bool EnableReranking { get; set; } = true;
    /// <summary>Number of candidate chunks passed to the reranker before trimming to <see cref="DefaultTopK"/>.</summary>
    public int RerankTopK { get; set; } = 20;

    // Retrieval
    /// <summary>Number of top-ranked chunks sent to the LLM as context when generating the final answer.</summary>
    public int DefaultTopK { get; set; } = 7;

    // GPU / ONNX execution provider
    /// <summary>Selects the ONNX Runtime execution provider used for embedding and reranking inference.</summary>
    public OnnxExecutionProvider ExecutionProviderPreference { get; set; } = OnnxExecutionProvider.Cpu;
    /// <summary>CUDA device index to use when <see cref="ExecutionProviderPreference"/> enables GPU inference.</summary>
    public int GpuDeviceId { get; set; } = 0;

    // OCR
    /// <summary>Starting DPI resolution for OCR attempts; the engine retries at higher resolutions if text extraction fails.</summary>
    public int OcrBaseDpi { get; set; } = 150;
    /// <summary>Maximum DPI resolution used as a final OCR attempt on low-quality images.</summary>
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
}
