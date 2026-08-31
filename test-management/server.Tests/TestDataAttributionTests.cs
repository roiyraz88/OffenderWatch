using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 7.22 — test_data_created ingestion through RunOrchestrator's real persistence path (its test seam), explicit-ownership rules only.</summary>
public class TestDataAttributionTests : TestDatabaseFixture
{
    private readonly RunOrchestrator _sut;

    public TestDataAttributionTests()
    {
        _sut = new RunOrchestrator(
            Db,
            Options.Create(new RunnerOptions()),
            new StubHostEnvironment(),
            TestHubContext.Real(),
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
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private static OwEvent Discovered(string externalId) => new()
    {
        Version = 1,
        EventType = "scenario_discovered",
        Runner = "pytest",
        TimestampUtc = DateTime.UtcNow,
        ExternalId = externalId,
        Name = externalId,
        Suite = "API",
    };

    private static OwEvent TestDataCreated(string scenarioExternalId, string entityType, string? entityExternalId, string? entityIdentifier) => new()
    {
        Version = 1,
        EventType = "test_data_created",
        Runner = "pytest",
        TimestampUtc = DateTime.UtcNow,
        ExternalId = scenarioExternalId,
        EntityType = entityType,
        EntityExternalId = entityExternalId,
        EntityIdentifier = entityIdentifier,
    };

    [Fact]
    public async Task TestDataCreated_CreatesActiveRecord()
    {
        var run = await SeedRunAsync();
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::creates_offender"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api,
            TestDataCreated("api::creates_offender", "Offender", "77", "AUTO12345"));

        var record = Db.TestDataRecords.Single(r => r.TestRunId == run.Id);
        Assert.Equal(TestDataCleanupStatus.Active, record.CleanupStatus);
        Assert.Equal(TestDataEntityType.Offender, record.EntityType);
        Assert.Equal("77", record.ExternalId);
        Assert.Equal("AUTO12345", record.Identifier);
    }

    [Fact]
    public async Task TestDataCreated_AttributesToTheCorrectRunAndScenario()
    {
        var run1 = await SeedRunAsync();
        var run2 = await SeedRunAsync();
        await _sut.ApplyEventForTestingAsync(run1.Id, RunnerKind.Api, Discovered("api::attribution_test"));
        await _sut.ApplyEventForTestingAsync(run2.Id, RunnerKind.Api, Discovered("api::attribution_test"));

        await _sut.ApplyEventForTestingAsync(run1.Id, RunnerKind.Api,
            TestDataCreated("api::attribution_test", "Offender", "1", "AUTO1"));

        var record = Db.TestDataRecords.Single();
        Assert.Equal(run1.Id, record.TestRunId);

        var expectedTestCase = Db.TestCases.Single(t => t.ExternalId == "api::attribution_test");
        var expectedScenario = Db.ScenarioResults.Single(sr => sr.TestRunId == run1.Id && sr.TestCaseId == expectedTestCase.Id);
        Assert.Equal(expectedScenario.Id, record.ScenarioResultId);

        // Never the other run's ScenarioResult for the same TestCase.
        var otherScenario = Db.ScenarioResults.Single(sr => sr.TestRunId == run2.Id && sr.TestCaseId == expectedTestCase.Id);
        Assert.NotEqual(otherScenario.Id, record.ScenarioResultId);
    }

    [Fact]
    public async Task TestDataCreated_UnresolvableScenario_StillRecordsRunOwnership_WithNullScenario()
    {
        var run = await SeedRunAsync();
        // No scenario_discovered was ever applied for this externalId in this run.

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api,
            TestDataCreated("api::never_discovered", "Offender", "5", "AUTO5"));

        var record = Db.TestDataRecords.Single();
        Assert.Equal(run.Id, record.TestRunId);
        Assert.Null(record.ScenarioResultId);
    }

    [Fact]
    public async Task TestDataCreated_UnknownEntityType_IsIgnoredNotThrown()
    {
        var run = await SeedRunAsync();
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::bad_entity_type"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api,
            TestDataCreated("api::bad_entity_type", "NotARealEntityType", "1", "AUTO1"));

        Assert.Empty(Db.TestDataRecords);
    }

    [Fact]
    public async Task TestDataCreated_LocationPointWithNoExternalId_IsStillRegisteredForInspection()
    {
        // The real target API's POST .../locations response carries no id at
        // all (verified live) -- ExternalId null is expected and valid.
        var run = await SeedRunAsync();
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, Discovered("api::location_test"));

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api,
            TestDataCreated("api::location_test", "LocationPoint", null, "offenderId=42"));

        var record = Db.TestDataRecords.Single();
        Assert.Equal(TestDataEntityType.LocationPoint, record.EntityType);
        Assert.Null(record.ExternalId);
        Assert.Equal("offenderId=42", record.Identifier);
    }

    private class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "OffenderWatch.TestManagement.Server.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
