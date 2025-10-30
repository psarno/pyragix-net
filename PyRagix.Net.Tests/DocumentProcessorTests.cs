using PyRagix.Net.Config;
using PyRagix.Net.Ingestion;
using Xunit;

namespace PyRagix.Net.Tests;

public class DocumentProcessorTests
{
    [Fact]
    public async Task ExtractTextAsync_WithHtmlFile_CleansMarkupAndWhitespace()
    {
        var config = new PyRagixConfig();
        using var processor = new DocumentProcessor(config);

        var fileName = $"{Guid.NewGuid():N}.html";
        try
        {
            var html = """
                <html>
                  <head>
                    <title>Ignored</title>
                    <style>body { color: red; }</style>
                    <script>console.log("ignored");</script>
                  </head>
                  <body>
                    <h1> Heading </h1>
                    <p>Some    text</p>
                  </body>
                </html>
                """;

            File.WriteAllText(fileName, html);

            var text = await processor.ExtractTextAsync(fileName);

            Assert.Equal("Heading Some text", text);
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    [Fact]
    public async Task ExtractTextAsync_WithUnsupportedExtension_Throws()
    {
        var config = new PyRagixConfig();
        using var processor = new DocumentProcessor(config);

        var fileName = $"{Guid.NewGuid():N}.xyz";
        File.WriteAllText(fileName, "content");

        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractTextAsync(fileName));
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }
}
