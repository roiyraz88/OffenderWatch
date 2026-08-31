using System.Collections.Concurrent;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// Lets POST /api/runs/{id}/stop reach the background worker actually
/// executing that run (4.5). One <see cref="CancellationTokenSource"/> per
/// live/queued RunId; registered as soon as the run is created (so a Stop
/// arriving before the worker even dequeues it still works) and removed
/// once the orchestrator finishes with it.
/// </summary>
public class RunCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _tokens = new();

    public CancellationToken Register(int runId)
    {
        var cts = _tokens.GetOrAdd(runId, _ => new CancellationTokenSource());
        return cts.Token;
    }

    /// <summary>Returns true if a live token was found and cancelled.</summary>
    public bool RequestCancel(int runId)
    {
        if (_tokens.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void Unregister(int runId)
    {
        if (_tokens.TryRemove(runId, out var cts))
        {
            cts.Dispose();
        }
    }
}
