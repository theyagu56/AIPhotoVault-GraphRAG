namespace PhotoVault.Core.Pipeline;

public interface IMediaProcessingQueue
{
    ValueTask EnqueueAsync(MediaProcessingJob job, CancellationToken ct = default);
    ValueTask<MediaProcessingJob> DequeueAsync(CancellationToken ct = default);
    int Count { get; }
}
