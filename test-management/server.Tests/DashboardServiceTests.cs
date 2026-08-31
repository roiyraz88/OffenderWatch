using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 8.18 — TM-07 dashboard aggregation, entirely against seeded local data; never the real target application.</summary>
public class DashboardServiceTests : TestDatabaseFixture
{
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _sut = new DashboardService(Db, new TestHistoryService(Db));
    }

    private async Task<TestRun> SeedRunAsync(
        string envName,
        RunStatus status,
        int passed = 0,
        int failed = 0,
        int expectedFailed = 0,
        int skipped = 0,
        DateTime? createdAtUtc = null,
        DateTime? startedAtUtc = null,
        DateTime? endedAtUtc = null)
    {
        var created = createdAtUtc ?? DateTime.UtcNow;
        var run = new TestRun
        {
            EnvironmentNameSnapshot = envName,
            BaseUrlSnapshot = $"https://{envName}.example.com",
            Status = status,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = created,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            PassedCount = passed,
            FailedCount = failed,
            ExpectedFailedCount = expectedFailed,
            SkippedCount = skipped,
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private async Task<TestCase> SeedTestCaseAsync(string externalId, string? bugId = null)
    {
        var testCase = new TestCase { ExternalId = externalId, Name = externalId, Suite = TestSuite.Api, BugId = bugId, CreatedAtUtc = DateTime.UtcNow };
        Db.TestCases.Add(testCase);
        await Db.SaveChangesAsync();
        return testCase;
    }

    private async Task<ScenarioResult> SeedResultAsync(TestRun run, TestCase testCase, ScenarioStatus status, DateTime? startedAtUtc = null, string? failureMessage = null)
    {
        var result = new ScenarioResult
        {
            TestRunId = run.Id,
            TestCaseId = testCase.Id,
            Status = status,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = startedAtUtc,
            FailureMessage = failureMessage,
        };
        Db.ScenarioResults.Add(result);
        await Db.SaveChangesAsync();
        return result;
    }

    // ---- Latest Run per Environment (8.3) --------------------------------

    [Fact]
    public async Task LatestRunsByEnvironment_PicksTheMostRecentTerminalRunPerEnvironment()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, createdAtUtc: t0);
        var newer = await SeedRunAsync("Dev", RunStatus.Completed, passed: 2, createdAtUtc: t0.AddHours(1));
        await SeedRunAsync("Staging", RunStatus.Completed, passed: 3, createdAtUtc: t0);

        var dashboard = await _sut.GetAsync();

        var devRow = dashboard.LatestRunsByEnvironment.Single(r => r.EnvironmentNameSnapshot == "Dev");
        Assert.Equal(newer.Id, devRow.RunId);
        Assert.Equal(2, devRow.PassedCount);
        Assert.Contains(dashboard.LatestRunsByEnvironment, r => r.EnvironmentNameSnapshot == "Staging");
    }

    [Fact]
    public async Task LatestRunsByEnvironment_IgnoresStillRunningRows()
    {
        var t0 = DateTime.UtcNow;
        var completed = await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, createdAtUtc: t0);
        await SeedRunAsync("Dev", RunStatus.Running, createdAtUtc: t0.AddMinutes(5)); // newer, but not terminal

        var dashboard = await _sut.GetAsync();

        var devRow = dashboard.LatestRunsByEnvironment.Single(r => r.EnvironmentNameSnapshot == "Dev");
        Assert.Equal(completed.Id, devRow.RunId);
    }

    [Fact]
    public async Task LatestRunsByEnvironment_PreservesHistoricalSnapshot_EvenThoughEnvironmentRowIsGone()
    {
        // No Environment row is ever seeded here at all — TestRun.EnvironmentId
        // stays null (exactly what happens after a real deletion, Step 3's
        // SET NULL) — the dashboard must still show the snapshot correctly.
        var run = await SeedRunAsync("DeletedEnv", RunStatus.Completed, passed: 5);

        var dashboard = await _sut.GetAsync();

        var row = dashboard.LatestRunsByEnvironment.Single(r => r.EnvironmentNameSnapshot == "DeletedEnv");
        Assert.Equal(run.Id, row.RunId);
        Assert.Equal("https://DeletedEnv.example.com", row.BaseUrlSnapshot);
    }

    // ---- Pass rate (8.4) --------------------------------------------------

    [Fact]
    public async Task PassRate_ExpectedFailCountsInDenominator_NotNumerator()
    {
        var run = await SeedRunAsync("Dev", RunStatus.Completed, passed: 7, failed: 0, expectedFailed: 26);

        var dashboard = await _sut.GetAsync();

        var row = dashboard.LatestRunsByEnvironment.Single();
        // 7 / 33 = 21.2%
        Assert.Equal(21.2, row.PassRate);
    }

    [Fact]
    public async Task PassRate_ExcludesSkipped()
    {
        var run = await SeedRunAsync("Dev", RunStatus.Completed, passed: 5, failed: 0, expectedFailed: 0, skipped: 95);

        var dashboard = await _sut.GetAsync();

        Assert.Equal(100.0, dashboard.LatestRunsByEnvironment.Single().PassRate);
    }

    [Fact]
    public async Task PassRate_ExcludesCancelled_BecauseTheyAreNeverInAnyOfTheFourPersistedCounts()
    {
        // Cancelled scenarios are simply never counted in Passed/Failed/
        // ExpectedFailed/Skipped (Step 4) — a run with real Cancelled
        // scenarios still yields a correct denominator from just those four.
        var run = await SeedRunAsync("Dev", RunStatus.Stopped, passed: 3, failed: 0, expectedFailed: 0);

        var dashboard = await _sut.GetAsync();

        Assert.Equal(100.0, dashboard.LatestRunsByEnvironment.Single(r => r.RunId == run.Id).PassRate);
    }

    [Fact]
    public async Task PassRate_ZeroDenominator_IsNull_NeverOneHundredPercent()
    {
        var run = await SeedRunAsync("Dev", RunStatus.Completed);

        var dashboard = await _sut.GetAsync();

        Assert.Null(dashboard.LatestRunsByEnvironment.Single().PassRate);
    }

    // ---- Trend (8.5) -------------------------------------------------------

    [Fact]
    public async Task Trend_IsChronological_OldestFirst()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var run3 = await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, createdAtUtc: t0.AddHours(2));
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, createdAtUtc: t0);
        var run2 = await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, createdAtUtc: t0.AddHours(1));

        var dashboard = await _sut.GetAsync();

        Assert.Equal(new[] { run1.Id, run2.Id, run3.Id }, dashboard.PassRateTrend.Select(p => p.RunId));
    }

    [Fact]
    public async Task Trend_UsesOnlyRealPersistedRunsWithAtLeastOneComparableResult()
    {
        await SeedRunAsync("Dev", RunStatus.Queued); // never ran — no comparable results at all
        await SeedRunAsync("Dev", RunStatus.Failed); // infra failure before anything finished
        var real = await SeedRunAsync("Dev", RunStatus.Completed, passed: 2, failed: 1);

        var dashboard = await _sut.GetAsync();

        var point = Assert.Single(dashboard.PassRateTrend);
        Assert.Equal(real.Id, point.RunId);
    }

    // ---- Currently failing (8.6) -------------------------------------------

    [Fact]
    public async Task CurrentlyFailing_UsesLatestComparableResult()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0);
        var run2 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0.AddHours(1));
        var testCase = await SeedTestCaseAsync("api::currently_failing_test");
        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Failed, t0.AddHours(1), failureMessage: "boom");

        var dashboard = await _sut.GetAsync();

        var entry = Assert.Single(dashboard.CurrentlyFailingTests, t => t.ExternalId == "api::currently_failing_test");
        Assert.Equal("Failed", entry.CurrentStatus);
        Assert.Equal("boom", entry.LatestFailureMessage);
        Assert.Equal(run2.Id, entry.LatestRunId);
    }

    [Fact]
    public async Task CurrentlyFailing_SkippedAndCancelledDoNotHideAnExistingFailure()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0);
        var run2 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0.AddHours(1));
        var run3 = await SeedRunAsync("Dev", RunStatus.Stopped, createdAtUtc: t0.AddHours(2));
        var testCase = await SeedTestCaseAsync("api::still_failing_through_neutral");
        await SeedResultAsync(run1, testCase, ScenarioStatus.Failed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Skipped, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Cancelled, t0.AddHours(2));

        var dashboard = await _sut.GetAsync();

        Assert.Contains(dashboard.CurrentlyFailingTests, t => t.ExternalId == "api::still_failing_through_neutral");
    }

    [Fact]
    public async Task CurrentlyFailing_RecoveredTestDisappears()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0);
        var run2 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0.AddHours(1));
        var testCase = await SeedTestCaseAsync("api::recovered_test");
        await SeedResultAsync(run1, testCase, ScenarioStatus.Failed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Passed, t0.AddHours(1));

        var dashboard = await _sut.GetAsync();

        Assert.DoesNotContain(dashboard.CurrentlyFailingTests, t => t.ExternalId == "api::recovered_test");
    }

    [Fact]
    public async Task CurrentlyFailing_ReusesStep6CurrentFailureSince_AndComputesDuration()
    {
        var t0 = DateTime.UtcNow.AddHours(-3);
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0);
        var testCase = await SeedTestCaseAsync("api::duration_test");
        await SeedResultAsync(run1, testCase, ScenarioStatus.Failed, t0);

        var dashboard = await _sut.GetAsync();

        var entry = Assert.Single(dashboard.CurrentlyFailingTests, t => t.ExternalId == "api::duration_test");
        Assert.Equal(run1.Id, entry.CurrentFailureSinceRunId);
        Assert.NotNull(entry.FailureDurationSeconds);
        Assert.True(entry.FailureDurationSeconds >= TimeSpan.FromHours(2.9).TotalSeconds);
    }

    [Fact]
    public async Task CurrentlyFailing_ExpectedFailIsDistinctFromFailed()
    {
        var t0 = DateTime.UtcNow;
        var run1 = await SeedRunAsync("Dev", RunStatus.Completed, createdAtUtc: t0);
        var testCase = await SeedTestCaseAsync("api::known_defect_test", bugId: "BUG-001");
        await SeedResultAsync(run1, testCase, ScenarioStatus.ExpectedFail, t0);

        var dashboard = await _sut.GetAsync();

        var entry = Assert.Single(dashboard.CurrentlyFailingTests, t => t.ExternalId == "api::known_defect_test");
        Assert.Equal("ExpectedFail", entry.CurrentStatus);
    }

    // ---- Go / No-Go / Incomplete / NoData (8.9) ----------------------------

    [Fact]
    public async Task Decision_NoData_WhenNoRunsExist()
    {
        var dashboard = await _sut.GetAsync();
        Assert.Equal("NoData", dashboard.OverallDecision);
        Assert.Null(dashboard.LatestRelevantRunId);
    }

    [Fact]
    public async Task Decision_Go_WhenLatestRunCompletedWithZeroUnexpectedFailures()
    {
        await SeedRunAsync("Dev", RunStatus.Completed, passed: 7, failed: 0, expectedFailed: 26);

        var dashboard = await _sut.GetAsync();

        Assert.Equal("Go", dashboard.OverallDecision);
    }

    [Fact]
    public async Task Decision_NoGo_WhenLatestRunHasUnexpectedFailures()
    {
        await SeedRunAsync("Dev", RunStatus.Completed, passed: 5, failed: 1, expectedFailed: 2);

        var dashboard = await _sut.GetAsync();

        Assert.Equal("NoGo", dashboard.OverallDecision);
    }

    [Fact]
    public async Task Decision_NoGo_WhenLatestRunIsInfrastructureFailed()
    {
        await SeedRunAsync("Dev", RunStatus.Failed);

        var dashboard = await _sut.GetAsync();

        Assert.Equal("NoGo", dashboard.OverallDecision);
    }

    [Fact]
    public async Task Decision_Incomplete_WhenLatestRunIsStopped_NeverFalseGo()
    {
        await SeedRunAsync("Dev", RunStatus.Stopped, passed: 10, failed: 0);

        var dashboard = await _sut.GetAsync();

        Assert.Equal("Incomplete", dashboard.OverallDecision);
        Assert.NotEqual("Go", dashboard.OverallDecision);
    }

    [Fact]
    public async Task Decision_UsesOnlyTheSingleMostRecentRun_AcrossAllEnvironments()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedRunAsync("Dev", RunStatus.Completed, passed: 1, failed: 5, createdAtUtc: t0); // older, would be NoGo
        await SeedRunAsync("Staging", RunStatus.Completed, passed: 1, failed: 0, createdAtUtc: t0.AddHours(1)); // newer, Go

        var dashboard = await _sut.GetAsync();

        Assert.Equal("Go", dashboard.OverallDecision);
    }
}
