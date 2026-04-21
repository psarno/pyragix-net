using System;
using System.IO;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Enumerates the coarse stages that the ingestion pipeline moves through so UIs can present meaningful status.
/// </summary>
public enum IngestionStage
{
    /// <summary>Initial scan of the target folder before files are discovered.</summary>
    Scanning,
    /// <summary>Existing indexes and database are being cleared for a fresh ingestion run.</summary>
    Resetting,
    /// <summary>Supported files have been enumerated and the total count is known.</summary>
    Discovery,
    /// <summary>A single file has begun processing.</summary>
    FileStarted,
    /// <summary>The file has been extracted and split into chunks.</summary>
    Chunking,
    /// <summary>Chunks are being converted to embedding vectors.</summary>
    Embedding,
    /// <summary>Vectors and metadata are being written to FAISS/Lucene/SQLite.</summary>
    Indexing,
    /// <summary>A file was skipped because it produced no extractable content.</summary>
    FileSkipped,
    /// <summary>All pipeline stages for a single file have finished successfully.</summary>
    FileCompleted,
    /// <summary>The FAISS index is being flushed to disk.</summary>
    Persisting,
    /// <summary>The entire ingestion run has finished.</summary>
    Completed,
    /// <summary>An unrecoverable error occurred while processing a file.</summary>
    Error
}

/// <summary>
/// Snapshot of the ingestion pipeline that communicates progress, diagnostics, and per-file metadata to observers.
/// </summary>
public sealed record IngestionProgressUpdate
{
    /// <summary>Coarse pipeline stage that this update represents.</summary>
    public required IngestionStage Stage { get; init; }
    /// <summary>Human-readable description of the current operation, suitable for display in a UI or console.</summary>
    public string? Message { get; init; }
    /// <summary>Number of files that have been fully processed so far in the current run.</summary>
    public int FilesCompleted { get; init; }
    /// <summary>Total number of supported files discovered at the start of the run.</summary>
    public int TotalFiles { get; init; }
    /// <summary>Absolute or relative path of the file currently being processed, or <see langword="null"/> for run-level updates.</summary>
    public string? CurrentFile { get; init; }
    /// <summary>Total number of chunks produced from <see cref="CurrentFile"/>, available from the <see cref="IngestionStage.Chunking"/> stage onward.</summary>
    public int? CurrentFileChunks { get; init; }
    /// <summary>Number of chunks from <see cref="CurrentFile"/> that have been embedded and indexed so far.</summary>
    public int? CurrentFileChunksProcessed { get; init; }
    /// <summary>Cumulative count of chunks written to the indexes across all files, set on <see cref="IngestionStage.Completed"/>.</summary>
    public long? TotalChunksIndexed { get; init; }
    /// <summary>Exception that caused an <see cref="IngestionStage.Error"/> update; <see langword="null"/> for non-error stages.</summary>
    public Exception? ExceptionDetail { get; init; }

    /// <summary>Creates an update indicating the folder scan has started.</summary>
    public static IngestionProgressUpdate Scanning(string folderPath) => new()
    {
        Stage = IngestionStage.Scanning,
        Message = $"Scanning {folderPath}..."
    };

    /// <summary>Creates an update indicating existing indexes are being cleared.</summary>
    public static IngestionProgressUpdate Resetting() => new()
    {
        Stage = IngestionStage.Resetting,
        Message = "Resetting existing indexes..."
    };

    /// <summary>Creates an update reporting how many supported files were found.</summary>
    public static IngestionProgressUpdate Discovery(int totalFiles, string folderPath) => new()
    {
        Stage = IngestionStage.Discovery,
        TotalFiles = totalFiles,
        Message = totalFiles == 0
            ? $"No supported documents found under {folderPath}."
            : $"Found {totalFiles} supported {(totalFiles == 1 ? "document" : "documents")}."
    };

    /// <summary>Creates an update signalling that processing has begun for a specific file.</summary>
    public static IngestionProgressUpdate FileStarted(string filePath, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileStarted,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Processing {Path.GetFileName(filePath)}"
    };

    /// <summary>Creates an update reporting the chunk count produced from a file.</summary>
    public static IngestionProgressUpdate Chunking(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Chunking,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Chunking {Path.GetFileName(filePath)} ({chunkCount} chunks)"
    };

    /// <summary>Creates an update indicating that embeddings are being generated for the file's chunks.</summary>
    public static IngestionProgressUpdate Embedding(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Embedding,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Embedding {chunkCount} chunks"
    };

    /// <summary>Creates an update indicating that chunks are being written to the search indexes.</summary>
    public static IngestionProgressUpdate Indexing(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Indexing,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Indexing {chunkCount} chunks"
    };

    /// <summary>Creates an update explaining why a file was skipped without producing any chunks.</summary>
    public static IngestionProgressUpdate FileSkipped(string filePath, string reason, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileSkipped,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Skipping {Path.GetFileName(filePath)} ({reason})"
    };

    /// <summary>Creates an update confirming that all pipeline stages for a file have completed successfully.</summary>
    public static IngestionProgressUpdate FileCompleted(string filePath, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileCompleted,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Finished {Path.GetFileName(filePath)}"
    };

    /// <summary>Creates an update indicating the FAISS index is being saved to disk.</summary>
    public static IngestionProgressUpdate Persisting(int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Persisting,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = "Persisting FAISS index..."
    };

    /// <summary>Creates a final summary update after all files have been processed.</summary>
    public static IngestionProgressUpdate Completed(int filesCompleted, int totalFiles, long totalChunksIndexed) => new()
    {
        Stage = IngestionStage.Completed,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        TotalChunksIndexed = totalChunksIndexed,
        Message = $"Ingestion complete: {filesCompleted} {(filesCompleted == 1 ? "file" : "files")}, {totalChunksIndexed} chunks indexed."
    };

    /// <summary>Creates an error update capturing the exception that stopped a file from being processed.</summary>
    public static IngestionProgressUpdate Error(string filePath, Exception exception, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Error,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        ExceptionDetail = exception,
        Message = $"Error processing {Path.GetFileName(filePath)} - {exception.Message}"
    };
}
