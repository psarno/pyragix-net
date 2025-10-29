using PyRagix.Net.Core;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("PyRagix.Net - Local RAG Engine\n");

        // Load configuration and create engine
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

    private static async Task IngestAsync(RagEngine engine, string folderPath)
    {
        Console.WriteLine($"Ingesting documents from: {folderPath}\n");

        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"Error: Folder does not exist: {folderPath}");
            return;
        }

        await engine.IngestDocumentsAsync(folderPath);
        Console.WriteLine("\nIngestion complete!");
    }

    private static async Task QueryAsync(RagEngine engine, string question)
    {
        Console.WriteLine($"Question: {question}\n");
        Console.WriteLine("Retrieving relevant documents and generating answer...\n");

        var answer = await engine.QueryAsync(question);

        Console.WriteLine("=== ANSWER ===");
        Console.WriteLine(answer);
    }
}
