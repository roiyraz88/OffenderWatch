using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Hubs;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 5.16 — the real-time (TM-03) layer's own focused tests.</summary>
public class RealTimeTests : TestDatabaseFixture
{
    [Fact]
    public void GroupName_IsRunPrefixedById()
    {
        Assert.Equal("run:42", RunHub.GroupName(42));
    }

    [Fact]
    public void ToSummaryDto_MapsRunUpdatedFields()
    {
        var run = new TestRun
        {
            Id = 7,
            EnvironmentNameSnapshot = "Roie",
            BaseUrlSnapshot = "https://example.com",
            Status = RunStatus.Running,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            PassedCount = 3,
            FailedCount = 1,
            ExpectedFailedCount = 2,
            SkippedCount = 0,
        };

        var dto = RunDtoMapper.ToSummaryDto(run);

        Assert.Equal(7, dto.Id);
        Assert.Equal("Running", dto.Status);
        Assert.Equal(3, dto.PassedCount);
        Assert.Equal(1, dto.FailedCount);
        Assert.Equal(2, dto.ExpectedFailedCount);
        Assert.Equal(0, dto.SkippedCount);
        Assert.Null(dto.DurationSeconds); // no EndedAtUtc yet
    }

    [Fact]
    public void ToScenarioResultDto_MapsScenarioUpdatedFields()
    {
        var testCase = new TestCase
        {
            Id = 5,
            ExternalId = "api::test_x",
            Name = "test_x",
            Suite = TestSuite.Api,
            RequirementId = "API-01",
            BugId = "BUG-001",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var sr = new ScenarioResult
        {
            Id = 99,
            TestRunId = 1,
            TestCaseId = 5,
            TestCase = testCase,
            Status = ScenarioStatus.ExpectedFail,
            DurationMs = 123,
            FailureMessage = "assert 1 == 2",
        };

        var dto = RunDtoMapper.ToScenarioResultDto(sr);

        Assert.Equal(99, dto.Id);
        Assert.Equal("api::test_x", dto.ExternalId);
        Assert.Equal("Api", dto.Suite);
        Assert.Equal("API-01", dto.RequirementId);
        Assert.Equal("BUG-001", dto.BugId);
        Assert.Equal("ExpectedFail", dto.Status);
        Assert.Equal(123, dto.DurationMs);
        Assert.Equal("assert 1 == 2", dto.FailureMessage);
    }

    [Fact]
    public async Task SignalRTransportFailure_DoesNotMarkRunFailed()
    {
        // 5.5 — every broadcast in this run throws; the run's persisted
        // outcome must be completely unaffected.
        var orchestrator = new RunOrchestrator(
            Db,
            Options.Create(new RunnerOptions()),
            new StubHostEnvironment(),
            new ThrowingHubContext(),
            NullLogger<RunOrchestrator>.Instance);

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

        var discovered = new OwEvent
        {
            Version = 1,
            EventType = "scenario_discovered",
            Runner = "pytest",
            TimestampUtc = DateTime.UtcNow,
            ExternalId = "api::broadcast_failure_test",
            Name = "broadcast_failure_test",
            Suite = "API",
        };
        var finished = new OwEvent
        {
            Version = 1,
            EventType = "scenario_finished",
            Runner = "pytest",
            TimestampUtc = DateTime.UtcNow,
            ExternalId = "api::broadcast_failure_test",
            Status = "passed",
            DurationMs = 50,
        };

        // None of these should throw despite every SignalR send failing —
        // SafeBroadcastAsync must swallow it.
        await orchestrator.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, discovered);
        await orchestrator.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, finished);
        await orchestrator.FinalizeForTestingAsync(run.Id, RunStatus.Completed);

        var reloaded = Db.TestRuns.Single(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Completed, reloaded.Status);
        Assert.Equal(1, reloaded.PassedCount);

        var result = Db.ScenarioResults.Single(sr => sr.TestRunId == run.Id);
        Assert.Equal(ScenarioStatus.Passed, result.Status);
    }

    private class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "OffenderWatch.TestManagement.Server.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
