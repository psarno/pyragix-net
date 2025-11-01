using PyRagix.Net.Core;
using PyRagix.Net.Ingestion;

/// <summary>
/// Minimal console host that exposes ingestion and querying commands for the PyRagix.Net engine.
/// </summary>
internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("PyRagix.Net - Local RAG Engine\n");

        // Load configuration and create engine so both commands share the same instance.
        var engine = RagEngine.FromSettings("../pyragix-net/settings.toml");

        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  pyragix-net-console ingest <folder_path>");
            Console.WriteLine("  pyragix-net-console query <question>");
            return;
        }

        var command = args[0].ToLower();

        try
        {
            switch (command)
            {
                case "ingest":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Please provide a folder path to ingest");
                        return;
                    }
                    await IngestAsync(engine, args[1]);
                    break;

                case "query":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Please provide a question to query");
                        return;
                    }
                    var question = string.Join(" ", args.Skip(1));
                    await QueryAsync(engine, question);
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Runs the ingestion pipeline against the specified folder.
    /// </summary>
    private static async Task IngestAsync(RagEngine engine, string folderPath)
    {
        Console.WriteLine($"Ingesting documents from: {folderPath}");
        Console.WriteLine();

        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"Error: Folder does not exist: {folderPath}");
            return;
        }

        using var consoleProgress = new pyragix_net_console.ConsoleIngestionProgress();

        try
        {
            var finalUpdate = await consoleProgress.RunAsync(engine.IngestDocumentsAsync(folderPath, progress: consoleProgress));
            Console.WriteLine(finalUpdate.Message);
        }
        catch
        {
            var lastUpdate = consoleProgress.LastUpdate;
            if (lastUpdate.Stage == IngestionStage.Error && !string.IsNullOrWhiteSpace(lastUpdate.Message))
            {
                Console.WriteLine(lastUpdate.Message);
            }

            throw;
        }
    }

    /// <summary>
    /// Executes a single interactive question against the engine and prints the answer.
    /// </summary>
    private static async Task QueryAsync(RagEngine engine, string question)
    {
        Console.WriteLine($"Question: {question}\n");
        Console.WriteLine("Retrieving relevant documents and generating answer...\n");

        var answer = await engine.QueryAsync(question);

        Console.WriteLine("=== ANSWER ===");
        Console.WriteLine(answer);
    }
}
