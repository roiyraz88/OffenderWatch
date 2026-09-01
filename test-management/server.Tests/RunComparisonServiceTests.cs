using Microsoft.Extensions.Logging.Abstractions;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Bonus B-02 — Run Comparison, against a seeded DB and the real RunService/RunComparisonService pair.</summary>
public class RunComparisonServiceTests : TestDatabaseFixture
{
    private readonly RunService _runService;
    private readonly RunComparisonService _sut;

    public RunComparisonServiceTests()
    {
        _runService = new RunService(Db, new RunQueue(), new RunCancellationRegistry(), TestHubContext.Real(), NullLogger<RunService>.Instance);
        _sut = new RunComparisonService(_runService);
    }

    private async Task<TestRun> SeedRunAsync(
        string environmentNameSnapshot = "Dev",
        string baseUrlSnapshot = "https://example.com",
        RunStatus status = RunStatus.Completed)
    {
        var run = new TestRun
        {
            EnvironmentNameSnapshot = environmentNameSnapshot,
            BaseUrlSnapshot = baseUrlSnapshot,
            Status = status,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = status == RunStatus.Completed ? DateTime.UtcNow.AddSeconds(30) : null,
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private async Task<TestCase> SeedTestCaseAsync(string externalId, string? requirementId = null)
    {
        var testCase = new TestCase
        {
            ExternalId = externalId,
            Name = externalId,
            Suite = TestSuite.Api,
            RequirementId = requirementId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.TestCases.Add(testCase);
        await Db.SaveChangesAsync();
        return testCase;
    }

    private async Task SeedResultAsync(TestRun run, TestCase testCase, ScenarioStatus status)
    {
        Db.ScenarioResults.Add(new ScenarioResult
        {
            TestRunId = run.Id,
            TestCaseId = testCase.Id,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
        // Keep run totals honest — the real orchestrator does this at
        // finalize time (RunOrchestrator.FinalizeAsync); the comparison's
        // totals delta reads these snapshot columns, not a live recount.
        run.PassedCount = Db.ScenarioResults.Count(r => r.TestRunId == run.Id && r.Status == ScenarioStatus.Passed);
        run.FailedCount = Db.ScenarioResults.Count(r => r.TestRunId == run.Id && r.Status == ScenarioStatus.Failed);
        run.ExpectedFailedCount = Db.ScenarioResults.Count(r => r.TestRunId == run.Id && r.Status == ScenarioStatus.ExpectedFail);
        run.SkippedCount = Db.ScenarioResults.Count(r => r.TestRunId == run.Id && r.Status == ScenarioStatus.Skipped);
        await Db.SaveChangesAsync();
    }

    // ---- classification (per-TestCase) ---------------------------------

    [Fact]
    public async Task PassedToFailed_IsRegression()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t1");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Failed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        var entry = Assert.Single(result.Tests);
        Assert.Equal("Regression", entry.Change);
        Assert.Equal(1, result.Summary.Regressions);
    }

    [Fact]
    public async Task FailedToPassed_IsRecovery()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t2");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Failed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        var entry = Assert.Single(result.Tests);
        Assert.Equal("Recovery", entry.Change);
        Assert.Equal(1, result.Summary.Recoveries);
    }

    [Fact]
    public async Task OnlyInCompareRun_IsNew()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t3");
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        var entry = Assert.Single(result.Tests);
        Assert.Equal("New", entry.Change);
        Assert.Null(entry.BaseStatus);
        Assert.Equal("Passed", entry.CompareStatus);
        Assert.Equal(1, result.Summary.New);
    }

    [Fact]
    public async Task OnlyInBaseRun_IsMissing()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t4");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        var entry = Assert.Single(result.Tests);
        Assert.Equal("Missing", entry.Change);
        Assert.Equal("Passed", entry.BaseStatus);
        Assert.Null(entry.CompareStatus);
        Assert.Equal(1, result.Summary.Missing);
    }

    [Fact]
    public async Task PassedToPassed_IsNotRegression()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t5");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal("StillPassing", Assert.Single(result.Tests).Change);
        Assert.Equal(0, result.Summary.Regressions);
    }

    [Fact]
    public async Task FailedToFailed_IsNotRecovery()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t6");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Failed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Failed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal("StillFailing", Assert.Single(result.Tests).Change);
        Assert.Equal(0, result.Summary.Recoveries);
    }

    [Fact]
    public async Task PassedToExpectedFail_IsNotAnUnexpectedRegression()
    {
        // The assignment's explicit warning: a known defect appearing is
        // never automatically an unexpected Regression.
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t7");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.ExpectedFail);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.NotEqual("Regression", Assert.Single(result.Tests).Change);
        Assert.Equal(0, result.Summary.Regressions);
    }

    [Fact]
    public async Task ExpectedFailToExpectedFail_IsExpectedFailureNotRegressionOrRecovery()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t8");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.ExpectedFail);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.ExpectedFail);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal("ExpectedFailure", Assert.Single(result.Tests).Change);
        Assert.Equal(0, result.Summary.Regressions);
        Assert.Equal(0, result.Summary.Recoveries);
    }

    [Fact]
    public async Task ExpectedFailToPassed_IsRecovery()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::t8b");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.ExpectedFail);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal("Recovery", Assert.Single(result.Tests).Change);
    }

    [Fact]
    public async Task Skipped_NeverProducesAFalseRegressionOrRecovery()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();

        var passedThenSkipped = await SeedTestCaseAsync("api::t9a");
        await SeedResultAsync(baseRun, passedThenSkipped, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, passedThenSkipped, ScenarioStatus.Skipped);

        var skippedThenFailed = await SeedTestCaseAsync("api::t9b");
        await SeedResultAsync(baseRun, skippedThenFailed, ScenarioStatus.Skipped);
        await SeedResultAsync(compareRun, skippedThenFailed, ScenarioStatus.Failed);

        var cancelledBoth = await SeedTestCaseAsync("api::t9c");
        await SeedResultAsync(baseRun, cancelledBoth, ScenarioStatus.Cancelled);
        await SeedResultAsync(compareRun, cancelledBoth, ScenarioStatus.Cancelled);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.DoesNotContain(result.Tests, t => t.Change is "Regression" or "Recovery");
        Assert.Equal(0, result.Summary.Regressions);
        Assert.Equal(0, result.Summary.Recoveries);
        Assert.Equal("Unchanged", result.Tests.Single(t => t.ExternalId == "api::t9c").Change);
    }

    // ---- totals / metadata ----------------------------------------------

    [Fact]
    public async Task TotalsDelta_IsComputedFromPersistedRunSnapshots()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();

        var t1 = await SeedTestCaseAsync("api::td1");
        var t2 = await SeedTestCaseAsync("api::td2");
        await SeedResultAsync(baseRun, t1, ScenarioStatus.Passed);
        await SeedResultAsync(baseRun, t2, ScenarioStatus.Failed);
        await SeedResultAsync(compareRun, t1, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, t2, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal(1, result.TotalsDelta.Passed.Base);
        Assert.Equal(2, result.TotalsDelta.Passed.Compare);
        Assert.Equal(1, result.TotalsDelta.Passed.Delta);
        Assert.Equal(1, result.TotalsDelta.Failed.Base);
        Assert.Equal(0, result.TotalsDelta.Failed.Compare);
        Assert.Equal(-1, result.TotalsDelta.Failed.Delta);
        Assert.Equal(2, result.TotalsDelta.Total.Base);
        Assert.Equal(2, result.TotalsDelta.Total.Compare);
    }

    [Fact]
    public async Task DifferentEnvironments_ComparisonStillWorks_AndIsFlagged()
    {
        var baseRun = await SeedRunAsync(environmentNameSnapshot: "Roie (Live Demo)", baseUrlSnapshot: "https://roie.example");
        var compareRun = await SeedRunAsync(environmentNameSnapshot: "Base Application", baseUrlSnapshot: "https://base-app.example");
        var testCase = await SeedTestCaseAsync("api::envdiff");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Passed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.True(result.EnvironmentsDiffer);
        Assert.Single(result.Tests); // comparison is NOT blocked
    }

    [Fact]
    public async Task SameEnvironment_IsNotFlaggedAsDiffering()
    {
        var baseRun = await SeedRunAsync(environmentNameSnapshot: "Roie (Live Demo)");
        var compareRun = await SeedRunAsync(environmentNameSnapshot: "Roie (Live Demo)");

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.False(result.EnvironmentsDiffer);
    }

    [Fact]
    public async Task UsesImmutableEnvironmentSnapshots_NotALiveEnvironmentEntity()
    {
        // No Environment rows exist at all in this test — BaseRun/CompareRun
        // carry only their own EnvironmentNameSnapshot/BaseUrlSnapshot, and
        // the comparison must still resolve entirely from those (7.20-style
        // guarantee, reused here): an Environment can be renamed or deleted
        // after the run without changing what the historical comparison shows.
        var baseRun = await SeedRunAsync(environmentNameSnapshot: "Deleted Env A", baseUrlSnapshot: "https://a.example");
        var compareRun = await SeedRunAsync(environmentNameSnapshot: "Deleted Env B", baseUrlSnapshot: "https://b.example");

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal("Deleted Env A", result.BaseRun.EnvironmentNameSnapshot);
        Assert.Equal("Deleted Env B", result.CompareRun.EnvironmentNameSnapshot);
        Assert.Null(result.BaseRun.EnvironmentId);
    }

    [Fact]
    public async Task NonexistentRun_ThrowsNotFound()
    {
        var baseRun = await SeedRunAsync();
        await Assert.ThrowsAsync<RunNotFoundException>(() => _sut.CompareAsync(baseRun.Id, 999999));
    }

    [Fact]
    public async Task SameRunOnBothSides_ThrowsValidation()
    {
        var run = await SeedRunAsync();
        await Assert.ThrowsAsync<RunComparisonValidationException>(() => _sut.CompareAsync(run.Id, run.Id));
    }

    [Fact]
    public async Task StoppedRun_IsFlaggedIncomplete_ButComparisonStillReturned()
    {
        var baseRun = await SeedRunAsync(status: RunStatus.Stopped);
        var compareRun = await SeedRunAsync(status: RunStatus.Completed);

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.True(result.BaseRunIncomplete);
        Assert.False(result.CompareRunIncomplete);
    }

    [Fact]
    public async Task RunWithNoScenarioResults_ProducesAnEmptyButValidComparison()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();

        var result = await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Empty(result.Tests);
        Assert.Equal(0, result.TotalsDelta.Total.Base);
        Assert.Equal(0, result.TotalsDelta.Total.Compare);
    }

    [Fact]
    public async Task Comparison_DoesNotModifyEitherRunOrItsScenarioResults()
    {
        var baseRun = await SeedRunAsync();
        var compareRun = await SeedRunAsync();
        var testCase = await SeedTestCaseAsync("api::readonly_check");
        await SeedResultAsync(baseRun, testCase, ScenarioStatus.Passed);
        await SeedResultAsync(compareRun, testCase, ScenarioStatus.Failed);

        var beforeBaseStatus = Db.ScenarioResults.Single(r => r.TestRunId == baseRun.Id).Status;
        var beforeCompareStatus = Db.ScenarioResults.Single(r => r.TestRunId == compareRun.Id).Status;

        await _sut.CompareAsync(baseRun.Id, compareRun.Id);

        Assert.Equal(2, Db.TestRuns.Count());
        Assert.Equal(beforeBaseStatus, Db.ScenarioResults.Single(r => r.TestRunId == baseRun.Id).Status);
        Assert.Equal(beforeCompareStatus, Db.ScenarioResults.Single(r => r.TestRunId == compareRun.Id).Status);
    }
}
