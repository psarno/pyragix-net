using PyRagix.Net.Config;
using System.Text.RegularExpressions;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Sentence-boundary-aware text chunking (like LangChain's RecursiveCharacterTextSplitter)
/// Respects semantic boundaries to preserve context
/// </summary>
public class SemanticChunker
{
    private readonly PyRagixConfig _config;

    public SemanticChunker(PyRagixConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Split text into chunks at sentence boundaries
    /// </summary>
    public List<string> ChunkText(string text)
    {
        if (!_config.EnableSemanticChunking)
        {
            // Fall back to simple fixed-size chunking
            return FixedChunk(text);
        }

        return SemanticChunk(text);
    }

    private List<string> SemanticChunk(string text)
    {
        var chunks = new List<string>();

        // Split into sentences
        var sentences = SplitSentences(text);

        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            var sentenceLength = sentence.Length;

            // If adding this sentence would exceed chunk size
            if (currentLength + sentenceLength > _config.ChunkSize && currentChunk.Count > 0)
            {
                // Save current chunk
                chunks.Add(string.Join(" ", currentChunk));

                // Start new chunk with overlap
                var overlapSentences = GetOverlapSentences(currentChunk, _config.ChunkOverlap);
                currentChunk = overlapSentences.ToList();
                currentLength = currentChunk.Sum(s => s.Length);
            }

            currentChunk.Add(sentence);
            currentLength += sentenceLength + 1; // +1 for space
        }

        // Add final chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }

    /// <summary>
    /// Split text into sentences using regex
    /// </summary>
    private List<string> SplitSentences(string text)
    {
        // Split on sentence boundaries: . ! ? followed by space or newline
        var pattern = @"(?<=[.!?])\s+(?=[A-Z])";
        var sentences = Regex.Split(text, pattern)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        return sentences;
    }

    /// <summary>
    /// Get last N characters worth of sentences for overlap
    /// </summary>
    private IEnumerable<string> GetOverlapSentences(List<string> sentences, int overlapSize)
    {
        var overlap = new List<string>();
        var length = 0;

        for (int i = sentences.Count - 1; i >= 0; i--)
        {
            if (length + sentences[i].Length > overlapSize)
                break;

            overlap.Insert(0, sentences[i]);
            length += sentences[i].Length + 1;
        }

        return overlap;
    }

    /// <summary>
    /// Simple fixed-size chunking (fallback)
    /// </summary>
    private List<string> FixedChunk(string text)
    {
        var chunks = new List<string>();
        var chunkSize = _config.ChunkSize;
        var overlap = _config.ChunkOverlap;

        for (int i = 0; i < text.Length; i += chunkSize - overlap)
        {
            var length = Math.Min(chunkSize, text.Length - i);
            chunks.Add(text.Substring(i, length));
        }

        return chunks;
    }
}
