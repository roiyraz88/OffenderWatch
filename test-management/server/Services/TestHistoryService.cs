using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// TM-04 (Step 6, Part A). Every value here is derived on read from the
/// existing stable TestCase -&gt; ScenarioResult -&gt; TestRun data — no
/// separate History table, nothing duplicated or persisted (6.1/6.2).
/// </summary>
public class TestHistoryService : ITestHistoryService
{
    private readonly TestManagementDbContext _db;

    public TestHistoryService(TestManagementDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TestCaseSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var testCases = await _db.TestCases.ToListAsync(ct);

        var summaries = new List<TestCaseSummaryDto>(testCases.Count);
        foreach (var testCase in testCases)
        {
            var chronological = await LoadChronologicalAsync(testCase.Id, ct);
            summaries.Add(BuildSummary(testCase, chronological));
        }

        return summaries
            .OrderBy(t => t.Suite)
            .ThenBy(t => t.Name)
            .ToList();
    }

    public async Task<TestCaseDetailDto> GetHistoryAsync(int testCaseId, CancellationToken ct = default)
    {
        var testCase = await _db.TestCases.FirstOrDefaultAsync(t => t.Id == testCaseId, ct)
            ?? throw new TestCaseNotFoundException(testCaseId);

        var chronological = await LoadChronologicalAsync(testCaseId, ct);
        var summary = BuildSummary(testCase, chronological);
        var transitions = HistoryClassifier.ComputeTransitions(chronological.Select(c => c.Result.Status).ToList());

        var history = new List<TestHistoryEntryDto>(chronological.Count);
        for (var i = 0; i < chronological.Count; i++)
        {
            var (result, run) = chronological[i];
            history.Add(new TestHistoryEntryDto
            {
                RunId = run.Id,
                EnvironmentNameSnapshot = run.EnvironmentNameSnapshot,
                RunStartedAtUtc = run.StartedAtUtc,
                ScenarioResultId = result.Id,
                Status = result.Status.ToString(),
                StartedAtUtc = result.StartedAtUtc,
                EndedAtUtc = result.EndedAtUtc,
                DurationMs = result.DurationMs,
                FailureMessage = result.FailureMessage,
                Transition = transitions[i],
            });
        }

        return new TestCaseDetailDto
        {
            Id = summary.Id,
            ExternalId = summary.ExternalId,
            Name = summary.Name,
            Suite = summary.Suite,
            RequirementId = summary.RequirementId,
            BugId = summary.BugId,
            LastStatus = summary.LastStatus,
            LastRunId = summary.LastRunId,
            LastExecutedAtUtc = summary.LastExecutedAtUtc,
            IsFlaky = summary.IsFlaky,
            CurrentFailureSinceRunId = summary.CurrentFailureSinceRunId,
            CurrentFailureSinceUtc = summary.CurrentFailureSinceUtc,
            LastPassRunId = summary.LastPassRunId,
            LastPassAtUtc = summary.LastPassAtUtc,
            History = history,
        };
    }

    /// <summary>
    /// Oldest-first. Ordered by the owning Run's CreatedAtUtc (the platform
    /// runs are strictly sequential — a single background worker, Step 4 —
    /// so run-creation order is execution order), then by ScenarioResult.Id
    /// as a stable tiebreaker.
    /// </summary>
    private async Task<List<(ScenarioResult Result, TestRun Run)>> LoadChronologicalAsync(int testCaseId, CancellationToken ct)
    {
        var rows = await _db.ScenarioResults
            .Where(sr => sr.TestCaseId == testCaseId)
            .Include(sr => sr.TestRun)
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.TestRun.CreatedAtUtc)
            .ThenBy(r => r.Id)
            .Select(r => (Result: r, Run: r.TestRun))
            .ToList();
    }

    private static TestCaseSummaryDto BuildSummary(TestCase testCase, List<(ScenarioResult Result, TestRun Run)> chronological)
    {
        var statuses = chronological.Select(c => c.Result.Status).ToList();

        var last = chronological.Count > 0 ? chronological[^1] : ((ScenarioResult, TestRun)?)null;

        var currentFailureSinceIndex = HistoryClassifier.ComputeCurrentFailureSinceIndex(statuses);
        var lastPassIndex = HistoryClassifier.ComputeLastPassIndex(statuses);

        return new TestCaseSummaryDto
        {
            Id = testCase.Id,
            ExternalId = testCase.ExternalId,
            Name = testCase.Name,
            Suite = testCase.Suite.ToString(),
            RequirementId = testCase.RequirementId,
            BugId = testCase.BugId,

            LastStatus = last?.Item1.Status.ToString(),
            LastRunId = last?.Item2.Id,
            LastExecutedAtUtc = last?.Item1.EndedAtUtc ?? last?.Item2.CreatedAtUtc,
            LastEnvironmentNameSnapshot = last?.Item2.EnvironmentNameSnapshot,
            LastFailureMessage = last?.Item1.FailureMessage,

            IsFlaky = HistoryClassifier.ComputeIsFlaky(statuses),

            CurrentFailureSinceRunId = currentFailureSinceIndex.HasValue ? chronological[currentFailureSinceIndex.Value].Item2.Id : null,
            CurrentFailureSinceUtc = currentFailureSinceIndex.HasValue
                ? (chronological[currentFailureSinceIndex.Value].Item1.StartedAtUtc ?? chronological[currentFailureSinceIndex.Value].Item2.CreatedAtUtc)
                : null,

            LastPassRunId = lastPassIndex.HasValue ? chronological[lastPassIndex.Value].Item2.Id : null,
            LastPassAtUtc = lastPassIndex.HasValue
                ? (chronological[lastPassIndex.Value].Item1.EndedAtUtc ?? chronological[lastPassIndex.Value].Item2.CreatedAtUtc)
                : null,
        };
    }
}
