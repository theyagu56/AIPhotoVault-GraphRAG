using System.Threading.Channels;

namespace PhotoVault.Core.Pipeline;

/// <summary>
/// Bounded in-memory channel queue (replace with Hangfire/Redis for scale).
/// </summary>
public sealed class InMemoryProcessingQueue : IMediaProcessingQueue
{
    private readonly Channel<MediaProcessingJob> _channel;

    public InMemoryProcessingQueue(int capacity = 500)
    {
        _channel = Channel.CreateBounded<MediaProcessingJob>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
    }

    public int Count => _channel.Reader.Count;

    public async ValueTask EnqueueAsync(MediaProcessingJob job, CancellationToken ct = default)
        => await _channel.Writer.WriteAsync(job, ct);

    public async ValueTask<MediaProcessingJob> DequeueAsync(CancellationToken ct = default)
        => await _channel.Reader.ReadAsync(ct);
}
