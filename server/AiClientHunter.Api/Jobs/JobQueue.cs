using System.Threading.Channels;

namespace AiClientHunter.Api.Jobs;

public class JobQueue
{
    private readonly Channel<long> _ch = Channel.CreateUnbounded<long>();

    public ValueTask EnqueueAsync(long jobId) => _ch.Writer.WriteAsync(jobId);

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}
