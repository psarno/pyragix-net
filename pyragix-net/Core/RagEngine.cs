using PyRagix.Net.Config;
using PyRagix.Net.Ingestion;
using PyRagix.Net.Retrieval;

namespace PyRagix.Net.Core;

/// <summary>
/// PyRagix.Net RAG Engine - Main entry point for consumers
/// Local-first RAG with query expansion, hybrid search, and reranking
/// </summary>
public class RagEngine : IDisposable
{
    private readonly PyRagixConfig _config;
    private IngestionService? _ingestionService;
    private RetrievalService? _retrievalService;

    /// <summary>
    /// Create RAG engine with configuration
    /// </summary>
    public RagEngine(PyRagixConfig config)
    {
        _config = config;
        _config.Validate();
    }

    /// <summary>
    /// Create RAG engine from TOML settings file
    /// </summary>
    public static RagEngine FromSettings(string settingsPath = "settings.toml")
    {
        var config = PyRagixConfig.LoadFromToml(settingsPath);
        return new RagEngine(config);
    }

    /// <summary>
    /// Ingest documents from a folder
    /// </summary>
    /// <param name="folderPath">Path to folder containing documents (PDF, HTML, images)</param>
    /// <param name="fresh">If true, creates new indexes. If false, appends to existing.</param>
    public async Task IngestDocumentsAsync(string folderPath, bool fresh = false)
    {
        _ingestionService ??= new IngestionService(_config);
        await _ingestionService.IngestFolderAsync(folderPath, fresh);
    }

    /// <summary>
    /// Query the RAG system
    /// </summary>
    /// <param name="question">Natural language question</param>
    /// <param name="topK">Number of top chunks to use for answer generation (default: 7)</param>
    /// <returns>Generated answer based on retrieved context</returns>
    public async Task<string> QueryAsync(string question, int? topK = null)
    {
        _retrievalService ??= new RetrievalService(_config);

        // Ensure system is ready
        if (!await _retrievalService.IsReadyAsync())
        {
            throw new InvalidOperationException("RAG system not ready. Check Ollama and index files.");
        }

        return await _retrievalService.QueryAsync(question, topK);
    }

    /// <summary>
    /// Check if system is ready for queries
    /// </summary>
    public async Task<bool> IsReadyAsync()
    {
        _retrievalService ??= new RetrievalService(_config);
        return await _retrievalService.IsReadyAsync();
    }

    public void Dispose()
    {
        _ingestionService?.Dispose();
        _retrievalService?.Dispose();
    }
}
