using UglyToad.PdfPig;
using AngleSharp;
using AngleSharp.Html.Parser;
using Tesseract;
using PyRagix.Net.Config;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Multi-format document processor (PDF, HTML, images via OCR)
/// </summary>
public class DocumentProcessor : IDisposable
{
    private readonly PyRagixConfig _config;
    private TesseractEngine? _ocrEngine;
    private readonly HtmlParser _htmlParser;

    public DocumentProcessor(PyRagixConfig config)
    {
        _config = config;
        _htmlParser = new HtmlParser();
    }

    /// <summary>
    /// Extract text from any supported document format
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
    /// Extract text from PDF using PdfPig
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
    /// Extract text from HTML using AngleSharp
    /// </summary>
    private async Task<string> ExtractFromHtmlAsync(string filePath)
    {
        var html = await File.ReadAllTextAsync(filePath);
        var document = await _htmlParser.ParseDocumentAsync(html);

        // Remove script and style tags
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
    /// Extract text from image using Tesseract OCR
    /// </summary>
    private async Task<string> ExtractFromImageAsync(string filePath)
    {
        return await Task.Run(() =>
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

                    // If we got reasonable text, return it
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
        });
    }

    /// <summary>
    /// Initialize Tesseract OCR engine (lazy)
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
    /// Clean extracted text (remove excessive whitespace)
    /// </summary>
    private string CleanText(string text)
    {
        // Normalize whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    public void Dispose()
    {
        _ocrEngine?.Dispose();
    }
}
