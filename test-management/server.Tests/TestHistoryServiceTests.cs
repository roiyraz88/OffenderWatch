using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 6.24 — TM-04 end to end through the real service against a seeded DB.</summary>
public class TestHistoryServiceTests : TestDatabaseFixture
{
    private readonly TestHistoryService _sut;

    public TestHistoryServiceTests()
    {
        _sut = new TestHistoryService(Db);
    }

    private async Task<TestRun> SeedRunAsync(DateTime createdAtUtc, string environmentNameSnapshot = "Dev")
    {
        var run = new TestRun
        {
            EnvironmentNameSnapshot = environmentNameSnapshot,
            BaseUrlSnapshot = "https://example.com",
            Status = RunStatus.Completed,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = createdAtUtc,
            StartedAtUtc = createdAtUtc,
            EndedAtUtc = createdAtUtc.AddSeconds(30),
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private async Task<TestCase> SeedTestCaseAsync(string externalId = "api::history_test", string? bugId = null)
    {
        var testCase = new TestCase
        {
            ExternalId = externalId,
            Name = externalId,
            Suite = TestSuite.Api,
            BugId = bugId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.TestCases.Add(testCase);
        await Db.SaveChangesAsync();
        return testCase;
    }

    private async Task SeedResultAsync(TestRun run, TestCase testCase, ScenarioStatus status, DateTime endedAtUtc)
    {
        Db.ScenarioResults.Add(new ScenarioResult
        {
            TestRunId = run.Id,
            TestCaseId = testCase.Id,
            Status = status,
            StartedAtUtc = endedAtUtc.AddSeconds(-1),
            EndedAtUtc = endedAtUtc,
        });
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetHistoryAsync_HistoryStaysInChronologicalOrder()
    {
        var testCase = await SeedTestCaseAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run3 = await SeedRunAsync(t0.AddHours(2));
        var run1 = await SeedRunAsync(t0);
        var run2 = await SeedRunAsync(t0.AddHours(1));

        await SeedResultAsync(run3, testCase, ScenarioStatus.Passed, t0.AddHours(2));
        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Failed, t0.AddHours(1));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        Assert.Equal(new[] { run1.Id, run2.Id, run3.Id }, detail.History.Select(h => h.RunId));
    }

    [Fact]
    public async Task GetHistoryAsync_ProducesRegressionThenRecoveryTransitions()
    {
        var testCase = await SeedTestCaseAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run1 = await SeedRunAsync(t0);
        var run2 = await SeedRunAsync(t0.AddHours(1));
        var run3 = await SeedRunAsync(t0.AddHours(2));

        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Failed, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Passed, t0.AddHours(2));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        Assert.Equal(new[] { "FirstResult", "Regression", "Recovery" }, detail.History.Select(h => h.Transition));
    }

    [Fact]
    public async Task GetHistoryAsync_CurrentFailureSinceAndLastPass_ResolveCorrectly()
    {
        var testCase = await SeedTestCaseAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run1 = await SeedRunAsync(t0);
        var run2 = await SeedRunAsync(t0.AddHours(1));
        var run3 = await SeedRunAsync(t0.AddHours(2));

        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Failed, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Failed, t0.AddHours(2));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        Assert.Equal(run2.Id, detail.CurrentFailureSinceRunId);
        Assert.Equal(run1.Id, detail.LastPassRunId);
        Assert.Equal("Failed", detail.LastStatus);
        Assert.Equal(run3.Id, detail.LastRunId);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryTestCaseWithDerivedSummary()
    {
        var testCase = await SeedTestCaseAsync("api::summary_test");
        var run = await SeedRunAsync(DateTime.UtcNow);
        await SeedResultAsync(run, testCase, ScenarioStatus.Passed, DateTime.UtcNow);

        var all = await _sut.GetAllAsync();

        var summary = Assert.Single(all, t => t.ExternalId == "api::summary_test");
        Assert.Equal("Passed", summary.LastStatus);
        Assert.Equal(run.Id, summary.LastRunId);
    }

    [Fact]
    public async Task GetHistoryAsync_UnknownTestCase_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<TestCaseNotFoundException>(() => _sut.GetHistoryAsync(999));
    }

    // ---- Environment-aware flakiness ---------------------------------

    [Fact]
    public async Task Flakiness_SameTestSameEnvironment_MultipleSwitches_IsFlaky()
    {
        var testCase = await SeedTestCaseAsync("api::flaky_same_env");
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run1 = await SeedRunAsync(t0, "Roie (Live Demo)");
        var run2 = await SeedRunAsync(t0.AddHours(1), "Roie (Live Demo)");
        var run3 = await SeedRunAsync(t0.AddHours(2), "Roie (Live Demo)");

        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Failed, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Passed, t0.AddHours(2));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        Assert.True(detail.IsFlaky);
    }

    [Fact]
    public async Task Flakiness_SameTestDifferentEnvironments_NoSwitchWithinEitherEnvironment_IsNotFlaky()
    {
        // Reproduces the reported scenario exactly: a real Pass streak on
        // the real target, with a single real Fail recorded only because a
        // different (controlled) Environment was used once in between for a
        // Regression/Recovery demonstration. Neither Environment's own
        // history alternates on its own.
        var testCase = await SeedTestCaseAsync("api::not_flaky_cross_env");
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run1 = await SeedRunAsync(t0, "Roie (Live Demo)");
        var run2 = await SeedRunAsync(t0.AddHours(1), "Roie (Live Demo)");
        var run3 = await SeedRunAsync(t0.AddHours(2), "Roie (Live Demo)");
        var run4 = await SeedRunAsync(t0.AddHours(3), "Local Regression Demo Target (not the real app)");
        var run5 = await SeedRunAsync(t0.AddHours(4), "Roie (Live Demo)");
        var run6 = await SeedRunAsync(t0.AddHours(5), "Roie (Live Demo)");

        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Passed, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Passed, t0.AddHours(2));
        await SeedResultAsync(run4, testCase, ScenarioStatus.Failed, t0.AddHours(3)); // Regression, controlled Environment
        await SeedResultAsync(run5, testCase, ScenarioStatus.Passed, t0.AddHours(4)); // Recovery, real Environment
        await SeedResultAsync(run6, testCase, ScenarioStatus.Passed, t0.AddHours(5));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        // The transitions themselves stay cross-environment and unchanged —
        // Run #4's Regression and Run #5's Recovery must still be visible.
        Assert.Equal(
            new[] { "FirstResult", "StillPassing", "StillPassing", "Regression", "Recovery", "StillPassing" },
            detail.History.Select(h => h.Transition));

        // But flakiness is scoped to the latest execution's own Environment
        // (Roie) — whose own results (Passed, Passed, Passed, Passed,
        // Passed — run4 excluded) never alternate at all.
        Assert.False(detail.IsFlaky);
    }

    [Fact]
    public async Task Flakiness_SkippedAndCancelled_RemainNeutral_AcrossEnvironmentFiltering()
    {
        var testCase = await SeedTestCaseAsync("api::flaky_neutral_check");
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var run1 = await SeedRunAsync(t0, "Roie (Live Demo)");
        var run2 = await SeedRunAsync(t0.AddHours(1), "Roie (Live Demo)");
        var run3 = await SeedRunAsync(t0.AddHours(2), "Roie (Live Demo)");
        var run4 = await SeedRunAsync(t0.AddHours(3), "Roie (Live Demo)");

        await SeedResultAsync(run1, testCase, ScenarioStatus.Passed, t0);
        await SeedResultAsync(run2, testCase, ScenarioStatus.Skipped, t0.AddHours(1));
        await SeedResultAsync(run3, testCase, ScenarioStatus.Cancelled, t0.AddHours(2));
        await SeedResultAsync(run4, testCase, ScenarioStatus.Passed, t0.AddHours(3));

        var detail = await _sut.GetHistoryAsync(testCase.Id);

        // Comparable sequence within the one environment is Passed, Passed —
        // zero switches — Skipped/Cancelled never counted either way.
        Assert.False(detail.IsFlaky);
    }
}
