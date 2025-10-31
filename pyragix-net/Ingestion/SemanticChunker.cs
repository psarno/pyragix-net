using PyRagix.Net.Config;
using System.Text.RegularExpressions;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Splits documents into overlapping sentence-aware chunks, preserving context for downstream retrieval.
/// Provides a fixed-window fallback when semantic chunking is disabled in configuration.
/// </summary>
public class SemanticChunker
{
    private readonly PyRagixConfig _config;

    public SemanticChunker(PyRagixConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Produces a list of chunk strings following the configuration's semantic/fixed settings.
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

            // If adding this sentence would exceed the chunk window, emit the existing chunk and carry forward overlap.
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
    /// Splits the input using a coarse sentence boundary heuristic similar to the Python implementation.
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
    /// Builds the overlap window by walking backwards through the current chunk until the desired character count is met.
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
    /// Fallback that emits fixed windows with the configured overlap when semantic chunking is disabled.
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
