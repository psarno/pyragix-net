using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Ingestion;

namespace PyRagix.Net.Retrieval;

/// <summary>
/// Orchestrates the full retrieval-augmented generation flow:
/// query expansion, hybrid retrieval, cross-encoder reranking, and answer generation.
/// </summary>
public class RetrievalService : IDisposable
{
    private readonly PyRagixConfig _config;
    private readonly PyRagixDbContext _dbContext;
    private readonly EmbeddingService _embeddingService;
    private readonly QueryExpander _queryExpander;
    private readonly HybridRetriever _hybridRetriever;
    private readonly Reranker _reranker;
    private readonly OllamaGenerator _generator;

    /// <summary>
    /// Constructs the service and spins up the reusable ingestion/retrieval dependencies.
    /// </summary>
    public RetrievalService(PyRagixConfig config)
    {
        _config = config;
        _dbContext = new PyRagixDbContext(config.DatabasePath);
        _embeddingService = new EmbeddingService(config);
        _queryExpander = new QueryExpander(config);
        _hybridRetriever = new HybridRetriever(config, _dbContext);
        _reranker = new Reranker(config);
        _generator = new OllamaGenerator(config);
    }

    /// <summary>
    /// Executes the full pipeline and returns the generated answer string.
    /// </summary>
    public async Task<string> QueryAsync(string question, int? topK = null)
    {
        var k = topK ?? _config.DefaultTopK;

        Console.WriteLine($"\nQuery: {question}");
        Console.WriteLine("=" + new string('=', 60));

        // Step 1: Query expansion
        Console.WriteLine("\n[1/4] Expanding query...");
        var queryVariants = await _queryExpander.ExpandQueryAsync(question);
        Console.WriteLine($"Generated {queryVariants.Count} query variants");

        // Step 2: Hybrid retrieval (for each variant)
        Console.WriteLine("\n[2/4] Retrieving documents...");
        var allChunks = new List<Core.Models.ChunkMetadata>();

        foreach (var variant in queryVariants)
        {
            var embedding = await _embeddingService.EmbedAsync(variant);
            var chunks = await _hybridRetriever.SearchAsync(embedding, variant, _config.RerankTopK);
            allChunks.AddRange(chunks);
        }

        // Deduplicate by chunk ID so reranking does not double-count context pulled by multiple variants.
        var uniqueChunks = allChunks
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"Retrieved {uniqueChunks.Count} unique chunks");

        // Step 3: Reranking
        Console.WriteLine("\n[3/4] Reranking results...");
        var rerankedChunks = await _reranker.RerankAsync(question, uniqueChunks);
        var topChunks = rerankedChunks.Take(k).ToList();

        Console.WriteLine($"Top {k} chunks after reranking:");
        for (int i = 0; i < topChunks.Count; i++)
        {
            var source = Path.GetFileName(topChunks[i].SourceFile);
            Console.WriteLine($"  {i + 1}. {source} (chunk {topChunks[i].ChunkIndex + 1}/{topChunks[i].TotalChunks})");
        }

        // Step 4: Generate answer
        Console.WriteLine("\n[4/4] Generating answer...");
        var answer = await _generator.GenerateAnswerAsync(question, topChunks);

        return answer;
    }

    /// <summary>
    /// Performs a readiness check before serving queries, ensuring Ollama and the FAISS index are reachable.
    /// </summary>
    public async Task<bool> IsReadyAsync()
    {
        var ollamaAvailable = await _generator.IsOllamaAvailableAsync();

        if (!ollamaAvailable)
        {
            Console.WriteLine("ERROR: Ollama is not running. Start with: ollama serve");
            return false;
        }

        if (!File.Exists(_config.FaissIndexPath))
        {
            Console.WriteLine("ERROR: FAISS index not found. Run ingestion first.");
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        _embeddingService?.Dispose();
        _hybridRetriever?.Dispose();
        _reranker?.Dispose();
        _dbContext?.Dispose();
    }
}
