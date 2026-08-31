using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// The single background consumer for <see cref="RunQueue"/> (4.16). Each
/// RunId gets its own DI scope (and therefore its own DbContext) resolved
/// fresh here — the HTTP request that created the run never holds a
/// DbContext open for the run's lifetime.
/// </summary>
public class RunExecutionBackgroundService : BackgroundService
{
    private readonly RunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RunCancellationRegistry _cancellation;
    private readonly ILogger<RunExecutionBackgroundService> _logger;

    public RunExecutionBackgroundService(
        RunQueue queue,
        IServiceScopeFactory scopeFactory,
        RunCancellationRegistry cancellation,
        ILogger<RunExecutionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _cancellation = cancellation;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var token = _cancellation.Register(runId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
                await orchestrator.RunAsync(runId, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run {RunId}: unhandled orchestration error", runId);
                await TryMarkFailedAsync(runId);
            }
            finally
            {
                _cancellation.Unregister(runId);
            }
        }
    }

    /// <summary>Best-effort recovery so a bug in the orchestrator can never leave a Run stuck in Queued/Running forever.</summary>
    private async Task TryMarkFailedAsync(int runId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestManagementDbContext>();
            var run = await db.TestRuns.FirstOrDefaultAsync(r => r.Id == runId);
            if (run is not null && run.Status is RunStatus.Queued or RunStatus.Running)
            {
                run.Status = RunStatus.Failed;
                run.EndedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId}: failed to record the orchestration error itself", runId);
        }
    }
}
