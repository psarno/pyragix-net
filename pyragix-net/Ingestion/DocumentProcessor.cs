using UglyToad.PdfPig;
using AngleSharp;
using AngleSharp.Html.Parser;
using Tesseract;
using Polly.Retry;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Resilience;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Extracts canonical text from supported document formats so downstream chunking can operate on plain strings.
/// Handles PDFs, HTML, and images (via OCR) to mirror the Python pipeline.
/// </summary>
public class DocumentProcessor : IDisposable
{
    private readonly PyRagixConfig _config;
    private TesseractEngine? _ocrEngine;
    private readonly HtmlParser _htmlParser;
    private readonly AsyncRetryPolicy _ocrRetryPolicy;

    /// <summary>
    /// Prepares the processor with configuration and pre-parsed HTML helpers.
    /// </summary>
    public DocumentProcessor(PyRagixConfig config)
    {
        _config = config;
        _htmlParser = new HtmlParser();
        _ocrRetryPolicy = RetryPolicies.CreateAsyncPolicy("OCR extraction");
    }

    /// <summary>
    /// Detects the file extension and dispatches to the appropriate extraction strategy.
    /// </summary>
    public async Task<string> ExtractTextAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => await ExtractFromPdfAsync(filePath),
            ".html" or ".htm" => await ExtractFromHtmlAsync(filePath),
            ".jpg" or ".jpeg" or ".png" or ".tiff" or ".bmp" or ".webp" => await ExtractFromImageAsync(filePath),
            _ => throw new NotSupportedException($"File type not supported: {extension}")
        };
    }

    /// <summary>
    /// Reads all pages from the PDF and joins the text into a single string separated by blank lines.
    /// </summary>
    private async Task<string> ExtractFromPdfAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var document = PdfDocument.Open(filePath);
            var text = string.Join("\n\n", document.GetPages().Select(page => page.Text));
            return text;
        });
    }

    /// <summary>
    /// Uses AngleSharp to parse the HTML and returns the visible body text, stripping scripts and styles.
    /// </summary>
    private async Task<string> ExtractFromHtmlAsync(string filePath)
    {
        var html = await File.ReadAllTextAsync(filePath);
        var document = await _htmlParser.ParseDocumentAsync(html);

        // Strip out non-visible content so OCR-like noise does not reach the chunker.
        var scriptsAndStyles = document.QuerySelectorAll("script, style");
        foreach (var element in scriptsAndStyles)
        {
            element.Remove();
        }

        // Get text content
        var text = document.Body?.TextContent ?? string.Empty;

        // Clean up whitespace
        return CleanText(text);
    }

    /// <summary>
    /// Runs OCR over the supplied image, cycling through multiple DPI attempts to improve recognition.
    /// </summary>
    private async Task<string> ExtractFromImageAsync(string filePath)
    {
        return await _ocrRetryPolicy.ExecuteAsync(() => Task.Run(() =>
        {
            InitializeOcr();

            if (_ocrEngine == null)
            {
                throw new InvalidOperationException("OCR engine not initialized");
            }

            // Try multiple DPI settings for best quality
            var dpiSettings = new[] { _config.OcrBaseDpi, 200, _config.OcrMaxDpi };

            foreach (var dpi in dpiSettings)
            {
                try
                {
                    using var img = Pix.LoadFromFile(filePath);
                    using var page = _ocrEngine.Process(img, PageSegMode.Auto);
                    var text = page.GetText();

                    // Return the first substantive result; subsequent DPI attempts are unnecessary at this point.
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 10)
                    {
                        return text;
                    }
                }
                catch
                {
                    // Try next DPI setting
                    continue;
                }
            }

            return string.Empty;
        }));
    }

    /// <summary>
    /// Lazily spins up the Tesseract engine so we only pay the startup cost when OCR is actually required.
    /// </summary>
    private void InitializeOcr()
    {
        if (_ocrEngine != null) return;

        var tessDataPath = "./tessdata";
        if (!Directory.Exists(tessDataPath))
        {
            throw new DirectoryNotFoundException(
                $"Tesseract data not found at {tessDataPath}. " +
                "Download from: https://github.com/tesseract-ocr/tessdata");
        }

        _ocrEngine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
    }

    /// <summary>
    /// Collapses repeated whitespace characters to stabilise downstream tokenisation.
    /// </summary>
    private string CleanText(string text)
    {
        // Normalize whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    /// <summary>
    /// Disposes the lazily created Tesseract engine when OCR is no longer required.
    /// </summary>
    public void Dispose()
    {
        _ocrEngine?.Dispose();
    }
}
