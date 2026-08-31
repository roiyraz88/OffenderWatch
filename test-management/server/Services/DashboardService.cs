using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// TM-07 (Step 8) — the one backend-derived release overview. Every number
/// here is computed on read from already-persisted, already-correct data:
/// Run-level pass rate reuses the exact four totals
/// <see cref="RunOrchestrator"/> finalizes each run with (Step 4) — never a
/// second re-derivation from raw ScenarioResults — and "currently failing"
/// reuses <see cref="ITestHistoryService"/>'s own CurrentFailureSince
/// output (Step 6) directly, rather than re-implementing
/// Regression/Recovery/CurrentFailureSince/"comparable result" a second
/// time.
/// </summary>
public class DashboardService : IDashboardService
{
    /// <summary>8.5 — the documented trend-length limit.</summary>
    private const int TrendLimit = 20;

    private readonly TestManagementDbContext _db;
    private readonly ITestHistoryService _testHistory;

    public DashboardService(TestManagementDbContext db, ITestHistoryService testHistory)
    {
        _db = db;
        _testHistory = testHistory;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var generatedAtUtc = DateTime.UtcNow;

        var allRunsDescending = await _db.TestRuns
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        var testSummaries = await _testHistory.GetAllAsync(ct);

        // 8.9 — the single most recently *created* Run, across every
        // Environment, is what the overall release decision judges. Not
        // per-environment: one platform-wide "what does the newest attempt
        // say" signal, exactly the "release overview" framing in 8.1/8.14.
        var latestRelevantRun = allRunsDescending.FirstOrDefault();

        var latestRunsByEnvironment = BuildLatestRunsByEnvironment(allRunsDescending);
        var trend = BuildTrend(allRunsDescending);
        var currentlyFailing = BuildCurrentlyFailing(testSummaries, generatedAtUtc);

        return new DashboardDto
        {
            GeneratedAtUtc = generatedAtUtc,
            OverallDecision = ComputeDecision(latestRelevantRun),
            LatestRelevantRunId = latestRelevantRun?.Id,
            LatestRunPassRate = latestRelevantRun is null
                ? null
                : PassRate(latestRelevantRun.PassedCount, latestRelevantRun.FailedCount, latestRelevantRun.ExpectedFailedCount),
            LatestRunUnexpectedFailedCount = latestRelevantRun?.FailedCount ?? 0,
            LatestRunExpectedFailedCount = latestRelevantRun?.ExpectedFailedCount ?? 0,
            CurrentlyFailingTestCount = currentlyFailing.Count,
            LatestRunsByEnvironment = latestRunsByEnvironment,
            PassRateTrend = trend,
            CurrentlyFailingTests = currentlyFailing,
        };
    }

    /// <summary>
    /// 8.4 — the one pass-rate formula used everywhere on the Dashboard:
    /// Passed / (Passed + Failed + ExpectedFail) * 100, Skipped/Cancelled
    /// excluded, null (never 100%) when the denominator is zero. The three
    /// inputs are TestRun's own persisted totals (Step 4's FinalizeAsync)
    /// — Cancelled scenarios are already excluded from all three there, so
    /// this is not a second definition, just this formula applied to the
    /// one existing source of truth.
    /// </summary>
    private static double? PassRate(int passed, int failed, int expectedFailed)
    {
        var denominator = passed + failed + expectedFailed;
        return denominator == 0 ? null : Math.Round(100.0 * passed / denominator, 1);
    }

    /// <summary>
    /// 8.3 — grouped by the immutable EnvironmentNameSnapshot (never the
    /// live Environment row, which may since have been edited or deleted),
    /// one row per group: its most recently created *relevant* Run — a Run
    /// that reached a terminal state (Completed/Stopped/Failed). A still
    /// Queued/Running Run isn't a picture of anything yet, so it's never
    /// chosen as "latest" here (documented dashboard rule, 8.3).
    /// </summary>
    private static List<DashboardEnvironmentRunDto> BuildLatestRunsByEnvironment(IReadOnlyList<TestRun> allRunsDescending)
    {
        return allRunsDescending
            .Where(r => r.Status is RunStatus.Completed or RunStatus.Stopped or RunStatus.Failed)
            .GroupBy(r => r.EnvironmentNameSnapshot)
            .Select(g => g.First()) // already newest-first from the caller's ordering
            .OrderBy(r => r.EnvironmentNameSnapshot, StringComparer.Ordinal)
            .Select(r => new DashboardEnvironmentRunDto
            {
                EnvironmentNameSnapshot = r.EnvironmentNameSnapshot,
                BaseUrlSnapshot = r.BaseUrlSnapshot,
                RunId = r.Id,
                Status = r.Status.ToString(),
                StartedAtUtc = r.StartedAtUtc,
                EndedAtUtc = r.EndedAtUtc,
                DurationSeconds = r.StartedAtUtc.HasValue && r.EndedAtUtc.HasValue
                    ? (r.EndedAtUtc.Value - r.StartedAtUtc.Value).TotalSeconds
                    : null,
                PassedCount = r.PassedCount,
                FailedCount = r.FailedCount,
                ExpectedFailedCount = r.ExpectedFailedCount,
                SkippedCount = r.SkippedCount,
                TotalScenarioCount = r.PassedCount + r.FailedCount + r.ExpectedFailedCount + r.SkippedCount,
                PassRate = PassRate(r.PassedCount, r.FailedCount, r.ExpectedFailedCount),
            })
            .ToList();
    }

    /// <summary>
    /// 8.5 — only Runs that produced at least one comparable result are
    /// meaningful trend points (excludes a Run that never started, and a
    /// Stopped/infrastructure-Failed Run that never got far enough to
    /// finish even one scenario) — a Stopped Run that DID complete real
    /// scenarios before being cancelled is still real, meaningful data and
    /// is kept. Latest 20 such Runs (documented limit), returned oldest
    /// first for a left-to-right chronological trend.
    /// </summary>
    private static List<DashboardTrendPointDto> BuildTrend(IReadOnlyList<TestRun> allRunsDescending)
    {
        return allRunsDescending
            .Where(r => r.PassedCount + r.FailedCount + r.ExpectedFailedCount > 0)
            .Take(TrendLimit)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new DashboardTrendPointDto
            {
                RunId = r.Id,
                EnvironmentNameSnapshot = r.EnvironmentNameSnapshot,
                TimestampUtc = r.StartedAtUtc ?? r.CreatedAtUtc,
                PassRate = PassRate(r.PassedCount, r.FailedCount, r.ExpectedFailedCount),
                PassedCount = r.PassedCount,
                FailedCount = r.FailedCount,
                ExpectedFailedCount = r.ExpectedFailedCount,
                TotalComparableCount = r.PassedCount + r.FailedCount + r.ExpectedFailedCount,
            })
            .ToList();
    }

    /// <summary>
    /// 8.6 — a TestCase is currently failing exactly when
    /// <see cref="ITestHistoryService"/> (Step 6) says its
    /// CurrentFailureSince streak is active (non-null) — the *same* check
    /// `/api/tests` already exposes, not a second one. Sorted longest-failing
    /// first, the most release-relevant ordering for a summary list.
    /// </summary>
    private static List<DashboardCurrentlyFailingTestDto> BuildCurrentlyFailing(
        IReadOnlyList<TestCaseSummaryDto> testSummaries, DateTime generatedAtUtc)
    {
        return testSummaries
            .Where(t => t.CurrentFailureSinceRunId.HasValue)
            .Select(t => new DashboardCurrentlyFailingTestDto
            {
                TestCaseId = t.Id,
                ExternalId = t.ExternalId,
                Name = t.Name,
                Suite = t.Suite,
                RequirementId = t.RequirementId,
                BugId = t.BugId,
                CurrentStatus = t.LastStatus ?? "Failed",
                LatestRunId = t.LastRunId ?? 0,
                LatestEnvironmentNameSnapshot = t.LastEnvironmentNameSnapshot ?? string.Empty,
                CurrentFailureSinceUtc = t.CurrentFailureSinceUtc,
                CurrentFailureSinceRunId = t.CurrentFailureSinceRunId,
                FailureDurationSeconds = t.CurrentFailureSinceUtc.HasValue
                    ? (generatedAtUtc - t.CurrentFailureSinceUtc.Value).TotalSeconds
                    : null,
                LatestFailureMessage = t.LastFailureMessage,
            })
            .OrderByDescending(t => t.FailureDurationSeconds ?? 0)
            .ToList();
    }

    /// <summary>
    /// 8.9 — deterministic, based only on the single latest (by
    /// CreatedAtUtc) Run across the whole platform:
    /// NoData: no Run exists at all.
    /// Incomplete: the latest Run hasn't reached a final, judgeable
    ///   outcome yet — Queued/Running (no result at all) or Stopped
    ///   (explicitly not a completed picture, 8.9's own requirement that a
    ///   Stopped Run must never be presented as a successful Go signal).
    /// NoGo: the latest Run ended with infrastructure Status=Failed, OR it
    ///   Completed with one or more *unexpected* Failed scenarios.
    /// Go: the latest Run Completed with zero unexpected Failed scenarios
    ///   (ExpectedFail never forces NoGo by itself — 8.9).
    /// </summary>
    private static string ComputeDecision(TestRun? latestRelevantRun)
    {
        if (latestRelevantRun is null)
        {
            return "NoData";
        }

        return latestRelevantRun.Status switch
        {
            RunStatus.Queued or RunStatus.Running or RunStatus.Stopped => "Incomplete",
            RunStatus.Failed => "NoGo",
            RunStatus.Completed => latestRelevantRun.FailedCount > 0 ? "NoGo" : "Go",
            _ => "NoData",
        };
    }
}
