using System;
using PyRagix.Net.Config;
using PyRagix.Net.Ingestion;
using PyRagix.Net.Retrieval;
using System.Threading;

namespace PyRagix.Net.Core;

/// <summary>
/// Public façade that wires together ingestion and retrieval services.
/// Callers hydrate this once and reuse it for document ingestion as well as conversational queries.
/// </summary>
public class RagEngine : IDisposable
{
    private readonly PyRagixConfig _config;
    private IngestionService? _ingestionService;
    private RetrievalService? _retrievalService;

    /// <summary>
    /// Creates a new engine instance using the supplied configuration and validates the settings eagerly.
    /// </summary>
    public RagEngine(PyRagixConfig config)
    {
        _config = config;
        _config.Validate();
    }

    /// <summary>
    /// Convenience constructor that loads configuration from disk before instantiating the engine.
    /// </summary>
    public static RagEngine FromSettings(string settingsPath = "settings.toml")
    {
        var config = PyRagixConfig.LoadFromToml(settingsPath);
        return new RagEngine(config);
    }

    /// <summary>
    /// Executes the full ingestion pipeline against every supported document inside the target folder.
    /// </summary>
    /// <param name="folderPath">Path to folder containing documents (PDF, HTML, images)</param>
    /// <param name="fresh">If true, creates new indexes. If false, appends to existing.</param>
    /// <param name="progress">Optional progress sink for reporting ingestion state.</param>
    /// <param name="cancellationToken">Token that cancels ingestion if requested.</param>
    public async Task IngestDocumentsAsync(string folderPath, bool fresh = false, IProgress<IngestionProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        _config.ValidateResources(ResourceValidationMode.Ingestion);
        _ingestionService ??= new IngestionService(_config);
        await _ingestionService.IngestFolderAsync(folderPath, fresh, progress, cancellationToken);
    }

    /// <summary>
    /// Runs the hybrid retrieval → rerank → generation pipeline for the supplied user question.
    /// </summary>
    /// <param name="question">Natural language question</param>
    /// <param name="topK">Number of top chunks to use for answer generation (default: 7)</param>
    /// <returns>Generated answer based on retrieved context</returns>
    public async Task<string> QueryAsync(string question, int? topK = null)
    {
        _config.ValidateResources(ResourceValidationMode.Retrieval);
        _retrievalService ??= new RetrievalService(_config);

        // Ensure critical dependencies (indexes + Ollama) are reachable before attempting retrieval.
        if (!await _retrievalService.IsReadyAsync())
        {
            throw new InvalidOperationException("RAG system not ready. Check Ollama and index files.");
        }

        return await _retrievalService.QueryAsync(question, topK);
    }

    /// <summary>
    /// Checks whether the engine has the minimum prerequisites to successfully answer a query.
    /// </summary>
    public async Task<bool> IsReadyAsync()
    {
        if (!_config.TryValidateResources(ResourceValidationMode.Retrieval, out var errors))
        {
            foreach (var error in errors)
            {
                Console.WriteLine($"ERROR: {error}");
            }
            return false;
        }

        _retrievalService ??= new RetrievalService(_config);
        return await _retrievalService.IsReadyAsync();
    }

    /// <summary>
    /// Disposes the lazily created services when the engine is no longer required.
    /// </summary>
    public void Dispose()
    {
        _ingestionService?.Dispose();
        _retrievalService?.Dispose();
    }
}
