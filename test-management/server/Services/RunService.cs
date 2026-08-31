using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
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

    public RunService(TestManagementDbContext db, RunQueue queue, RunCancellationRegistry cancellation)
    {
        _db = db;
        _queue = queue;
        _cancellation = cancellation;
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
        }
        // Otherwise (Running with a live token): the orchestrator itself
        // observes cancellation, kills the child process, marks pending
        // ScenarioResults Cancelled, and finalizes the Run to Stopped.
    }

    private static RunSummaryDto ToSummaryDto(TestRun r) => new()
    {
        Id = r.Id,
        EnvironmentId = r.EnvironmentId,
        EnvironmentNameSnapshot = r.EnvironmentNameSnapshot,
        BaseUrlSnapshot = r.BaseUrlSnapshot,
        Status = r.Status.ToString(),
        Trigger = r.Trigger.ToString(),
        CreatedAtUtc = r.CreatedAtUtc,
        StartedAtUtc = r.StartedAtUtc,
        EndedAtUtc = r.EndedAtUtc,
        DurationSeconds = r.StartedAtUtc.HasValue && r.EndedAtUtc.HasValue
            ? (r.EndedAtUtc.Value - r.StartedAtUtc.Value).TotalSeconds
            : null,
        PassedCount = r.PassedCount,
        FailedCount = r.FailedCount,
        ExpectedFailedCount = r.ExpectedFailedCount,
        SkippedCount = r.SkippedCount,
    };

    private static ScenarioResultDto ToScenarioResultDto(ScenarioResult sr) => new()
    {
        Id = sr.Id,
        TestCaseId = sr.TestCaseId,
        ExternalId = sr.TestCase.ExternalId,
        Name = sr.TestCase.Name,
        Suite = sr.TestCase.Suite.ToString(),
        RequirementId = sr.TestCase.RequirementId,
        BugId = sr.TestCase.BugId,
        Status = sr.Status.ToString(),
        StartedAtUtc = sr.StartedAtUtc,
        EndedAtUtc = sr.EndedAtUtc,
        DurationMs = sr.DurationMs,
        FailureMessage = sr.FailureMessage,
        StackTrace = sr.StackTrace,
    };
}
