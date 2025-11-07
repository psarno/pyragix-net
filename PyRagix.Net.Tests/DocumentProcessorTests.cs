using PyRagix.Net.Config;
using PyRagix.Net.Ingestion;
using UglyToad.PdfPig.Core;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Exercises the document processor to confirm HTML clean-up and unsupported format handling match expectations.
/// </summary>
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

            // Scripts/styles should be removed and whitespace collapsed for the chunker.
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
            // Any file type outside the known set should raise NotSupportedException.
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

    [Fact]
    public async Task ExtractTextAsync_WithCorruptedPdf_SurfacesPdfDocumentFormatException()
    {
        var config = new PyRagixConfig();
        using var processor = new DocumentProcessor(config);

        var fileName = $"{Guid.NewGuid():N}.pdf";
        File.WriteAllText(fileName, "not a real pdf");

        try
        {
            await Assert.ThrowsAsync<PdfDocumentFormatException>(() => processor.ExtractTextAsync(fileName));
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
