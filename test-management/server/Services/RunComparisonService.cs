using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// Bonus B-02 — Run Comparison. Entirely read-only: built on top of
/// <see cref="IRunService.GetByIdAsync"/> (the exact same persisted
/// RunSummary/ScenarioResult data the Run Details page already renders — no
/// separate DB query, no new table, nothing invented), so it automatically
/// reuses the existing stable TestCase identity (ScenarioResultDto.TestCaseId)
/// to match scenarios between the two runs instead of matching by display
/// name. Never mutates either run.
/// </summary>
public class RunComparisonService : IRunComparisonService
{
    private readonly IRunService _runs;

    public RunComparisonService(IRunService runs)
    {
        _runs = runs;
    }

    public async Task<RunComparisonDto> CompareAsync(int baseRunId, int compareRunId, CancellationToken ct = default)
    {
        if (baseRunId == compareRunId)
        {
            throw new RunComparisonValidationException("Base Run and Compare Run must be two different runs.");
        }

        // RunNotFoundException from GetByIdAsync already maps to 404 (9 —
        // "nonexistent run returns appropriate response") — reused as-is.
        var baseRun = await _runs.GetByIdAsync(baseRunId, ct);
        var compareRun = await _runs.GetByIdAsync(compareRunId, ct);

        var baseByTestCase = baseRun.ScenarioResults.ToDictionary(s => s.TestCaseId);
        var compareByTestCase = compareRun.ScenarioResults.ToDictionary(s => s.TestCaseId);

        var allTestCaseIds = baseByTestCase.Keys.Union(compareByTestCase.Keys);

        var summary = new ComparisonSummaryDto();
        var entries = new List<TestComparisonEntryDto>();

        foreach (var testCaseId in allTestCaseIds)
        {
            baseByTestCase.TryGetValue(testCaseId, out var baseResult);
            compareByTestCase.TryGetValue(testCaseId, out var compareResult);

            var baseStatus = ParseStatus(baseResult?.Status);
            var compareStatus = ParseStatus(compareResult?.Status);
            var change = RunComparisonClassifier.Classify(baseStatus, compareStatus);
            ApplyToSummary(summary, change);

            // Either side is a full ScenarioResultDto (same TestCase, same
            // Name/Suite/RequirementId/BugId — TestCase metadata is stable
            // across runs) — whichever one exists carries the display info.
            var reference = baseResult ?? compareResult!;

            entries.Add(new TestComparisonEntryDto
            {
                TestCaseId = testCaseId,
                ExternalId = reference.ExternalId,
                Name = reference.Name,
                Suite = reference.Suite,
                RequirementId = reference.RequirementId,
                BugId = reference.BugId,
                BaseStatus = baseResult?.Status,
                CompareStatus = compareResult?.Status,
                Change = change.ToString(),
            });
        }

        entries = entries
            .OrderBy(e => ChangePriority(e.Change))
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RunComparisonDto
        {
            BaseRun = ToSummary(baseRun),
            CompareRun = ToSummary(compareRun),
            EnvironmentsDiffer = baseRun.EnvironmentNameSnapshot != compareRun.EnvironmentNameSnapshot
                || baseRun.BaseUrlSnapshot != compareRun.BaseUrlSnapshot,
            BaseRunIncomplete = IsIncomplete(baseRun.Status),
            CompareRunIncomplete = IsIncomplete(compareRun.Status),
            TotalsDelta = new TotalsDeltaDto
            {
                Passed = Delta(baseRun.PassedCount, compareRun.PassedCount),
                Failed = Delta(baseRun.FailedCount, compareRun.FailedCount),
                ExpectedFail = Delta(baseRun.ExpectedFailedCount, compareRun.ExpectedFailedCount),
                Skipped = Delta(baseRun.SkippedCount, compareRun.SkippedCount),
                Total = Delta(RunTotal(baseRun), RunTotal(compareRun)),
            },
            Summary = summary,
            Tests = entries,
        };
    }

    private static int RunTotal(RunDetailDto run) =>
        run.PassedCount + run.FailedCount + run.ExpectedFailedCount + run.SkippedCount;

    private static MetricDeltaDto Delta(int baseValue, int compareValue) => new()
    {
        Base = baseValue,
        Compare = compareValue,
        Delta = compareValue - baseValue,
    };

    /// <summary>Only Completed represents a finished, complete suite (9 — "Stopped/Incomplete" warning).</summary>
    private static bool IsIncomplete(string status) => status != nameof(RunStatus.Completed);

    private static ScenarioStatus? ParseStatus(string? status) =>
        status is null ? null : Enum.Parse<ScenarioStatus>(status);

    private static void ApplyToSummary(ComparisonSummaryDto summary, ComparisonChangeType change)
    {
        switch (change)
        {
            case ComparisonChangeType.Regression: summary.Regressions++; break;
            case ComparisonChangeType.Recovery: summary.Recoveries++; break;
            case ComparisonChangeType.New: summary.New++; break;
            case ComparisonChangeType.Missing: summary.Missing++; break;
            case ComparisonChangeType.StillPassing: summary.StillPassing++; break;
            case ComparisonChangeType.StillFailing: summary.StillFailing++; break;
            case ComparisonChangeType.ExpectedFailure: summary.ExpectedFailures++; break;
            case ComparisonChangeType.Unchanged: summary.Unchanged++; break;
            case ComparisonChangeType.OtherChange: summary.OtherChanges++; break;
        }
    }

    /// <summary>The QA-relevant changes float to the top of the Test Differences table (7).</summary>
    private static int ChangePriority(string change) => change switch
    {
        nameof(ComparisonChangeType.Regression) => 0,
        nameof(ComparisonChangeType.Recovery) => 1,
        nameof(ComparisonChangeType.New) => 2,
        nameof(ComparisonChangeType.Missing) => 3,
        nameof(ComparisonChangeType.OtherChange) => 4,
        nameof(ComparisonChangeType.ExpectedFailure) => 5,
        nameof(ComparisonChangeType.StillFailing) => 6,
        nameof(ComparisonChangeType.StillPassing) => 7,
        _ => 8, // Unchanged
    };

    private static RunSummaryDto ToSummary(RunDetailDto run) => new()
    {
        Id = run.Id,
        EnvironmentId = run.EnvironmentId,
        EnvironmentNameSnapshot = run.EnvironmentNameSnapshot,
        BaseUrlSnapshot = run.BaseUrlSnapshot,
        Status = run.Status,
        Trigger = run.Trigger,
        CreatedAtUtc = run.CreatedAtUtc,
        StartedAtUtc = run.StartedAtUtc,
        EndedAtUtc = run.EndedAtUtc,
        DurationSeconds = run.DurationSeconds,
        PassedCount = run.PassedCount,
        FailedCount = run.FailedCount,
        ExpectedFailedCount = run.ExpectedFailedCount,
        SkippedCount = run.SkippedCount,
    };
}
