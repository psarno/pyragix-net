using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PyRagix.Net.Ingestion;

namespace pyragix_net_console;

/// <summary>
/// Renders ingestion progress to the console with a lightweight ASCII spinner and status line.
/// </summary>
internal sealed class ConsoleIngestionProgress : IProgress<IngestionProgressUpdate>, IDisposable
{
    private readonly object _syncLock = new();
    private readonly string[] _frames = ["-", "\\", "|", "/"];
    private readonly TimeSpan _frameInterval = TimeSpan.FromMilliseconds(120);
    private readonly CancellationTokenSource _renderCts = new();
    private Task? _renderTask;
    private IngestionProgressUpdate _latestUpdate = new() { Stage = IngestionStage.Scanning, Message = "Preparing ingestion..." };
    private int _lastLineLength;
    private int _frameIndex;
    private bool _disposed;

    /// <summary>
    /// Provides the most recent progress update for callers that need to surface diagnostics after rendering stops.
    /// </summary>
    public IngestionProgressUpdate LastUpdate
    {
        get
        {
            lock (_syncLock)
            {
                return _latestUpdate;
            }
        }
    }

    /// <summary>
    /// Receives progress updates from the ingestion pipeline.
    /// </summary>
    public void Report(IngestionProgressUpdate value)
    {
        lock (_syncLock)
        {
            _latestUpdate = value;
        }
    }

    /// <summary>
    /// Runs a render loop while the provided ingestion task is executing and returns the final update once complete.
    /// </summary>
    public async Task<IngestionProgressUpdate> RunAsync(Task ingestionTask)
    {
        _renderTask = Task.Run(() => RenderLoopAsync(_renderCts.Token));

        try
        {
            await ingestionTask.ConfigureAwait(false);
            return await StopRenderingAsync().ConfigureAwait(false);
        }
        catch
        {
            await StopRenderingAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IngestionProgressUpdate snapshot;
                lock (_syncLock)
                {
                    snapshot = _latestUpdate;
                }

                var frame = _frames[_frameIndex % _frames.Length];
                _frameIndex++;

                var line = BuildStatusLine(frame, snapshot);
                WriteInline(line);

                try
                {
                    await Task.Delay(_frameInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Rendering was cancelled because ingestion completed or failed.
        }
    }

    private string BuildStatusLine(string frame, IngestionProgressUpdate update)
    {
        var builder = new StringBuilder();
        builder.Append(frame).Append(' ');

        if (update.TotalFiles > 0)
        {
            var completed = Math.Clamp(update.FilesCompleted, 0, update.TotalFiles);
            builder.Append('[').Append(completed).Append('/').Append(update.TotalFiles).Append("] ");
        }

        if (!string.IsNullOrWhiteSpace(update.Message))
        {
            builder.Append(update.Message);
        }
        else
        {
            builder.Append(update.Stage switch
            {
                IngestionStage.Scanning => "Scanning documents...",
                IngestionStage.Resetting => "Resetting indexes...",
                IngestionStage.Discovery => "Preparing worklist...",
                IngestionStage.FileStarted => "Processing document...",
                IngestionStage.Chunking => "Chunking document...",
                IngestionStage.Embedding => "Embedding chunks...",
                IngestionStage.Indexing => "Indexing chunks...",
                IngestionStage.FileSkipped => "Skipping document...",
                IngestionStage.FileCompleted => "Document completed.",
                IngestionStage.Persisting => "Persisting indexes...",
                IngestionStage.Completed => "Ingestion complete.",
                IngestionStage.Error => "Error detected.",
                _ => "Working..."
            });
        }

        if (update.Stage == IngestionStage.Completed && update.TotalChunksIndexed.HasValue)
        {
            builder.Append(" (chunks: ").Append(update.TotalChunksIndexed.Value).Append(')');
        }

        if (update.Stage == IngestionStage.Error && update.ExceptionDetail != null)
        {
            builder.Append(" :: ").Append(update.ExceptionDetail.Message);
        }

        return builder.ToString();
    }

    private void WriteInline(string line)
    {
        lock (_syncLock)
        {
            Console.Write('\r');
            Console.Write(line);

            if (line.Length < _lastLineLength)
            {
                Console.Write(new string(' ', _lastLineLength - line.Length));
                Console.Write('\r');
                Console.Write(line);
            }

            _lastLineLength = line.Length;
        }
    }

    private async Task<IngestionProgressUpdate> StopRenderingAsync()
    {
        if (_renderTask == null)
        {
            return LastUpdate;
        }

        _renderCts.Cancel();

        try
        {
            await _renderTask.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Expected when cancellation token triggers.
        }

        IngestionProgressUpdate final;
        int lineLength;

        lock (_syncLock)
        {
            final = _latestUpdate;
            lineLength = _lastLineLength;
            _lastLineLength = 0;
        }

        Console.Write('\r');
        Console.Write(new string(' ', lineLength));
        Console.Write('\r');

        return final;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderCts.Cancel();
        _renderCts.Dispose();
    }
}
