using System.Threading.Channels;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// The "enqueue execution" half of 4.1 — POST /api/runs writes a RunId here
/// and returns immediately; <see cref="RunExecutionBackgroundService"/> is
/// the single consumer. A single queued worker is explicitly acceptable
/// (4.1); concurrent runs are not required.
/// </summary>
public class RunQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public ChannelWriter<int> Writer => _channel.Writer;
    public ChannelReader<int> Reader => _channel.Reader;
}
