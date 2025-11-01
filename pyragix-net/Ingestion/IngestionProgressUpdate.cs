using System;
using System.IO;

namespace PyRagix.Net.Ingestion;

/// <summary>
/// Enumerates the coarse stages that the ingestion pipeline moves through so UIs can present meaningful status.
/// </summary>
public enum IngestionStage
{
    Scanning,
    Resetting,
    Discovery,
    FileStarted,
    Chunking,
    Embedding,
    Indexing,
    FileSkipped,
    FileCompleted,
    Persisting,
    Completed,
    Error
}

/// <summary>
/// Snapshot of the ingestion pipeline that communicates progress, diagnostics, and per-file metadata to observers.
/// </summary>
public sealed record IngestionProgressUpdate
{
    public required IngestionStage Stage { get; init; }
    public string? Message { get; init; }
    public int FilesCompleted { get; init; }
    public int TotalFiles { get; init; }
    public string? CurrentFile { get; init; }
    public int? CurrentFileChunks { get; init; }
    public int? CurrentFileChunksProcessed { get; init; }
    public long? TotalChunksIndexed { get; init; }
    public Exception? ExceptionDetail { get; init; }

    public static IngestionProgressUpdate Scanning(string folderPath) => new()
    {
        Stage = IngestionStage.Scanning,
        Message = $"Scanning {folderPath}..."
    };

    public static IngestionProgressUpdate Resetting() => new()
    {
        Stage = IngestionStage.Resetting,
        Message = "Resetting existing indexes..."
    };

    public static IngestionProgressUpdate Discovery(int totalFiles, string folderPath) => new()
    {
        Stage = IngestionStage.Discovery,
        TotalFiles = totalFiles,
        Message = totalFiles == 0
            ? $"No supported documents found under {folderPath}."
            : $"Found {totalFiles} supported {(totalFiles == 1 ? "document" : "documents")}."
    };

    public static IngestionProgressUpdate FileStarted(string filePath, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileStarted,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Processing {Path.GetFileName(filePath)}"
    };

    public static IngestionProgressUpdate Chunking(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Chunking,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Chunking {Path.GetFileName(filePath)} ({chunkCount} chunks)"
    };

    public static IngestionProgressUpdate Embedding(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Embedding,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Embedding {chunkCount} chunks"
    };

    public static IngestionProgressUpdate Indexing(string filePath, int chunkCount, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Indexing,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        CurrentFileChunks = chunkCount,
        Message = $"Indexing {chunkCount} chunks"
    };

    public static IngestionProgressUpdate FileSkipped(string filePath, string reason, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileSkipped,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Skipping {Path.GetFileName(filePath)} ({reason})"
    };

    public static IngestionProgressUpdate FileCompleted(string filePath, int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.FileCompleted,
        CurrentFile = filePath,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = $"Finished {Path.GetFileName(filePath)}"
    };

    public static IngestionProgressUpdate Persisting(int filesCompleted, int totalFiles) => new()
    {
        Stage = IngestionStage.Persisting,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        Message = "Persisting FAISS index..."
    };

    public static IngestionProgressUpdate Completed(int filesCompleted, int totalFiles, long totalChunksIndexed) => new()
    {
        Stage = IngestionStage.Completed,
        FilesCompleted = filesCompleted,
        TotalFiles = totalFiles,
        TotalChunksIndexed = totalChunksIndexed,
        Message = $"Ingestion complete: {filesCompleted} {(filesCompleted == 1 ? "file" : "files")}, {totalChunksIndexed} chunks indexed."
    };

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
