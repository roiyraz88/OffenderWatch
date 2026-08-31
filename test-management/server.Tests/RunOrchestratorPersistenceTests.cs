using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Step 4.25 — the event-persistence and finalize logic that
/// <see cref="RunOrchestrator"/> would normally drive from real runner
/// output, exercised here through its test seams
/// (ApplyEventForTestingAsync/FinalizeForTestingAsync) so these are fast,
/// deterministic, and never spawn pytest/Playwright or touch the external
/// OffenderWatch demo site.
/// </summary>
public class RunOrchestratorPersistenceTests : TestDatabaseFixture
{
    private readonly RunOrchestrator _sut;

    public RunOrchestratorPersistenceTests()
    {
        _sut = new RunOrchestrator(
            Db,
            Options.Create(new RunnerOptions()),
            new StubHostEnvironment(),
            NullLogger<RunOrchestrator>.Instance);
    }

    private async Task<TestRun> SeedRunAsync()
    {
        var run = new TestRun
        {
            EnvironmentNameSnapshot = "Dev",
            BaseUrlSnapshot = "https://example.com",
            Status = RunStatus.Running,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private static OwEvent Discovered(string externalId, string? bugId = null) => new()
    {
        Version = 1,
        EventType = "scenario_discovered",
        Runner = "pytest",
        TimestampUtc = DateTime.UtcNow,
        ExternalId = externalId,
        Name = externalId,
        Suite = "API",
        BugId = bugId,
    };

    private static OwEvent Finished(string externalId, string status, long durationMs = 100) => new()
    {
        Version = 1,
        EventType = "scenario_finished",
        Runner = "pytest",
        TimestampUtc = DateTime.UtcNow,
        ExternalId = externalId,
        Status = status,
        DurationMs = durationMs,
    };

    [Fact]
    public async Task Discovered_CreatesOneTestCase_ReusedAcrossRuns()
    {
        var run1 = await SeedRunAsync();
        var run2 = await SeedRunAsync();

        await _sut.ApplyEventForTestingAsync(run1.Id, RunnerKind.Api, Discovered("api::same_test"));
        await _sut.ApplyEventForTestingAsync(run2.Id, RunnerKind.Api, Discovered("api::same_test"));

        Assert.Single(Db.TestCases, t => t.ExternalId == "api::same_test");
    }

    [Fact]
    public async Task Discovered_CreatesOneScenarioResultPerRunTestCasePair()
    {
        var run = await SeedRunAsync();

        // Applied twice for the same run — the (TestRunId, TestCaseId)
        // unique constraint must not be violated, and no duplicate row created.
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::dup_test"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::dup_test"));

        var testCase = Assert.Single(Db.TestCases, t => t.ExternalId == "api::dup_test");
        Assert.Single(Db.ScenarioResults, sr => sr.TestRunId == run.Id && sr.TestCaseId == testCase.Id);
    }

    [Fact]
    public async Task FailureOnKnownDefectTest_IsStoredAsExpectedFail()
    {
        var run = await SeedRunAsync();

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::known_bug_test", bugId: "BUG-042"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::known_bug_test", "failed"));

        var result = Db.ScenarioResults.Single(sr => sr.TestRunId == run.Id);
        Assert.Equal(ScenarioStatus.ExpectedFail, result.Status);
    }

    [Fact]
    public async Task FailureOnUnknownTest_IsStoredAsFailed()
    {
        var run = await SeedRunAsync();

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::plain_test"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::plain_test", "failed"));

        var result = Db.ScenarioResults.Single(sr => sr.TestRunId == run.Id);
        Assert.Equal(ScenarioStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Finalize_CalculatesTotalsFromPersistedScenarioResults()
    {
        var run = await SeedRunAsync();

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::t1"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::t1", "passed"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::t2", bugId: "BUG-001"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::t2", "failed"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::t3"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::t3", "failed"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::t4"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::t4", "skipped"));

        await _sut.FinalizeForTestingAsync(run.Id, RunStatus.Completed);

        var reloaded = Db.TestRuns.Single(r => r.Id == run.Id);
        Assert.Equal(1, reloaded.PassedCount);
        Assert.Equal(1, reloaded.FailedCount);
        Assert.Equal(1, reloaded.ExpectedFailedCount);
        Assert.Equal(1, reloaded.SkippedCount);
    }

    [Fact]
    public async Task Finalize_WithScenarioFailures_StillMarksRunCompleted()
    {
        // 4.2 — a Completed run may contain failed scenarios; the Run
        // itself is only ever Failed for infrastructure reasons, decided
        // by the caller (RunAsync), never inferred here from FailedCount.
        var run = await SeedRunAsync();

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::always_fails"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Finished("api::always_fails", "failed"));

        await _sut.FinalizeForTestingAsync(run.Id, RunStatus.Completed);

        var reloaded = Db.TestRuns.Single(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Completed, reloaded.Status);
        Assert.Equal(1, reloaded.FailedCount);
    }

    private class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "OffenderWatch.TestManagement.Server.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
