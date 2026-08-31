using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Hubs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// The HTTP-facing half of TM-02 (4.3–4.5). Creating and reading a run is
/// fast, synchronous DB work; actual execution is handed off to
/// <see cref="RunQueue"/> / <see cref="RunExecutionBackgroundService"/> so
/// POST /api/runs never blocks on the automation suites.
/// </summary>
public class RunService : IRunService
{
    private readonly TestManagementDbContext _db;
    private readonly RunQueue _queue;
    private readonly RunCancellationRegistry _cancellation;
    private readonly IHubContext<RunHub> _hub;
    private readonly ILogger<RunService> _logger;

    public RunService(
        TestManagementDbContext db,
        RunQueue queue,
        RunCancellationRegistry cancellation,
        IHubContext<RunHub> hub,
        ILogger<RunService> logger)
    {
        _db = db;
        _queue = queue;
        _cancellation = cancellation;
        _hub = hub;
        _logger = logger;
    }

    public async Task<RunSummaryDto> CreateAsync(CreateRunRequest request, CancellationToken ct = default)
    {
        // 4.3 — the Environment is the only source of the target URL; the
        // request never carries a BaseUrl of its own.
        var environment = await _db.Environments.FirstOrDefaultAsync(e => e.Id == request.EnvironmentId, ct);
        if (environment is null)
        {
            throw new EnvironmentNotFoundException(request.EnvironmentId);
        }

        var run = new TestRun
        {
            EnvironmentId = environment.Id,
            EnvironmentNameSnapshot = environment.Name,
            BaseUrlSnapshot = environment.BaseUrl,
            Status = RunStatus.Queued,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.TestRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Register before enqueuing: a Stop that races in immediately after
        // this call must always find a live token to cancel.
        _cancellation.Register(run.Id);
        await _queue.Writer.WriteAsync(run.Id, ct);

        return ToSummaryDto(run);
    }

    public async Task<IReadOnlyList<RunSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var runs = await _db.TestRuns
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return runs.Select(ToSummaryDto).ToList();
    }

    public async Task<RunDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var run = await _db.TestRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
        {
            throw new RunNotFoundException(id);
        }

        var scenarioResults = await _db.ScenarioResults
            .Where(sr => sr.TestRunId == id)
            .Include(sr => sr.TestCase)
            .OrderBy(sr => sr.Id)
            .ToListAsync(ct);

        var summary = ToSummaryDto(run);
        return new RunDetailDto
        {
            Id = summary.Id,
            EnvironmentId = summary.EnvironmentId,
            EnvironmentNameSnapshot = summary.EnvironmentNameSnapshot,
            BaseUrlSnapshot = summary.BaseUrlSnapshot,
            Status = summary.Status,
            Trigger = summary.Trigger,
            CreatedAtUtc = summary.CreatedAtUtc,
            StartedAtUtc = summary.StartedAtUtc,
            EndedAtUtc = summary.EndedAtUtc,
            DurationSeconds = summary.DurationSeconds,
            PassedCount = summary.PassedCount,
            FailedCount = summary.FailedCount,
            ExpectedFailedCount = summary.ExpectedFailedCount,
            SkippedCount = summary.SkippedCount,
            ScenarioResults = scenarioResults.Select(ToScenarioResultDto).ToList(),
        };
    }

    public async Task StopAsync(int id, CancellationToken ct = default)
    {
        var run = await _db.TestRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
        {
            throw new RunNotFoundException(id);
        }

        if (run.Status is RunStatus.Completed or RunStatus.Stopped or RunStatus.Failed)
        {
            // 4.5 — a clear conflict, not a silent no-op that could look
            // like it worked, and never touches already-finished history.
            throw new RunConflictException($"Run {id} has already finished ({run.Status}).");
        }

        var hadLiveToken = _cancellation.RequestCancel(id);

        if (run.Status == RunStatus.Queued)
        {
            // Never picked up yet (or racing with the worker) — flip it
            // directly so it can never start. If the worker does dequeue it
            // a moment later, RunOrchestrator's own Status!=Queued guard
            // turns that into a no-op.
            run.Status = RunStatus.Stopped;
            run.EndedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await BroadcastRunUpdatedAsync(run);
        }
        else if (run.Status == RunStatus.Running && !hadLiveToken)
        {
            // Orphaned Running row with no live token to cancel (e.g. after
            // a server restart mid-run, so there's no process to kill
            // either) — best-effort direct stop rather than leaving it
            // stuck forever.
            run.Status = RunStatus.Stopped;
            run.EndedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await BroadcastRunUpdatedAsync(run);
        }
        // Otherwise (Running with a live token): the orchestrator itself
        // observes cancellation, kills the child process, marks pending
        // ScenarioResults Cancelled, and finalizes the Run to Stopped —
        // and broadcasts both, per 5.6.
    }

    public async Task<IReadOnlyList<EvidenceArtifactDto>> GetScenarioEvidenceAsync(int runId, int scenarioResultId, CancellationToken ct = default)
    {
        var belongsToRun = await _db.ScenarioResults
            .AnyAsync(sr => sr.Id == scenarioResultId && sr.TestRunId == runId, ct);
        if (!belongsToRun)
        {
            throw new ScenarioResultNotFoundException(runId, scenarioResultId);
        }

        var artifacts = await _db.EvidenceArtifacts
            .Where(a => a.ScenarioResultId == scenarioResultId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        return artifacts.Select(a => new EvidenceArtifactDto
        {
            Id = a.Id,
            ScenarioResultId = a.ScenarioResultId,
            Type = a.Type.ToString(),
            ContentType = a.ContentType,
            SizeBytes = a.SizeBytes,
            CreatedAtUtc = a.CreatedAtUtc,
        }).ToList();
    }

    private static RunSummaryDto ToSummaryDto(TestRun r) => RunDtoMapper.ToSummaryDto(r);

    private static ScenarioResultDto ToScenarioResultDto(ScenarioResult sr) => RunDtoMapper.ToScenarioResultDto(sr);

    /// <summary>
    /// Same broadcast-after-persist, fail-soft discipline as
    /// <see cref="RunOrchestrator"/> (5.4/5.5) for the two paths above where
    /// this HTTP-facing service — not the orchestrator — is what changes a
    /// Run's status directly (a Queued run that never started, or an
    /// orphaned Running row with no live process to cancel).
    /// </summary>
    private async Task BroadcastRunUpdatedAsync(TestRun run)
    {
        try
        {
            await _hub.Clients.Group(RunHub.GroupName(run.Id)).SendAsync("RunUpdated", RunDtoMapper.ToSummaryDto(run));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId}: SignalR broadcast of RunUpdated failed (execution continues unaffected)", run.Id);
        }
    }
}
