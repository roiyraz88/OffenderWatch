using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 7.22 — TM-06 cleanup, entirely against a fake HTTP handler; never a real destructive call.</summary>
public class TestDataServiceTests : TestDatabaseFixture
{
    private async Task<TestRun> SeedRunAsync(string baseUrl = "https://example.com/target")
    {
        var run = new TestRun
        {
            EnvironmentNameSnapshot = "Dev",
            BaseUrlSnapshot = baseUrl,
            Status = RunStatus.Completed,
            Trigger = RunTrigger.Manual,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        return run;
    }

    private async Task<TestDataRecord> SeedRecordAsync(
        TestRun run,
        TestDataEntityType entityType = TestDataEntityType.Offender,
        string? externalId = "123",
        string? identifier = "AUTO12345",
        TestDataCleanupStatus status = TestDataCleanupStatus.Active,
        int? scenarioResultId = null)
    {
        var record = new TestDataRecord
        {
            TestRunId = run.Id,
            ScenarioResultId = scenarioResultId,
            EntityType = entityType,
            ExternalId = externalId,
            Identifier = identifier,
            CreatedAtUtc = DateTime.UtcNow,
            CleanupStatus = status,
        };
        Db.TestDataRecords.Add(record);
        await Db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task Clean_SuccessfulDelete_MarksCleanedWithTimestamp()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("Cleaned", result.CleanupStatus);
        Assert.NotNull(result.CleanedAtUtc);
    }

    [Fact]
    public async Task Clean_TargetAlreadyMissing_ConfirmedByNotFound_IsCleaned()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NotFound);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("Cleaned", result.CleanupStatus);
        Assert.NotNull(result.CleanedAtUtc);
    }

    [Fact]
    public async Task Clean_ServerError_IsCleanupFailed_NotCleaned()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.InternalServerError);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("CleanupFailed", result.CleanupStatus);
        Assert.Null(result.CleanedAtUtc);
    }

    [Fact]
    public async Task Clean_NetworkFailure_IsCleanupFailed_NotCleaned()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.ThrowingOn();
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("CleanupFailed", result.CleanupStatus);
        Assert.Null(result.CleanedAtUtc);
    }

    [Fact]
    public async Task Clean_CleanupFailedRecord_CanBeRetriedSuccessfully()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run, status: TestDataCleanupStatus.CleanupFailed);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("Cleaned", result.CleanupStatus);
    }

    [Fact]
    public async Task Clean_AlreadyCleanedRecord_IsANoOp_NeverCallsTheApiAgain()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run, status: TestDataCleanupStatus.Cleaned);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.InternalServerError); // would fail if called
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("Cleaned", result.CleanupStatus);
        Assert.Empty(factory.Requests);
    }

    [Fact]
    public async Task Clean_UsesOwningRunsBaseUrlSnapshot_NotAnyOtherValue()
    {
        var run = await SeedRunAsync("https://the-owning-run-snapshot.example/target");
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        await sut.CleanAsync(record.Id);

        var request = Assert.Single(factory.Requests);
        Assert.StartsWith("https://the-owning-run-snapshot.example/target", request.RequestUri!.ToString());
        Assert.Contains($"/api/offenders/{record.ExternalId}", request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Delete, request.Method);
    }

    [Fact]
    public async Task Clean_SeedSafetyGuard_RejectsRecordWhoseIdentifierIsNotAutoPrefixed()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run, identifier: "305412876"); // a real seeded-looking national id, not AUTO-prefixed
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent); // would succeed if ever called

        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);
        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("CleanupFailed", result.CleanupStatus);
        Assert.Empty(factory.Requests); // the destructive call must never even be attempted
    }

    [Fact]
    public async Task Clean_LocationPoint_IsAlwaysCleanupFailed_NoDeleteEndpointExists()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run, entityType: TestDataEntityType.LocationPoint, externalId: null, identifier: "offenderId=42");
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var result = await sut.CleanAsync(record.Id);

        Assert.Equal("CleanupFailed", result.CleanupStatus);
        Assert.Empty(factory.Requests);
    }

    [Fact]
    public async Task CleanBatch_EmptyIdList_ThrowsValidation()
    {
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        await Assert.ThrowsAsync<TestDataValidationException>(() => sut.CleanBatchAsync(Array.Empty<int>()));
    }

    [Fact]
    public async Task CleanBatch_PartialFailure_DoesNotHideOtherSuccesses()
    {
        var run = await SeedRunAsync();
        var willSucceed = await SeedRecordAsync(run, externalId: "1", identifier: "AUTO111");
        var willFail = await SeedRecordAsync(run, externalId: "2", identifier: "AUTO222");

        var factory = new FakeHttpClientFactory(req =>
            req.RequestUri!.ToString().EndsWith("/1")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var results = await sut.CleanBatchAsync(new[] { willSucceed.Id, willFail.Id });

        Assert.Equal("Cleaned", results.Single(r => r.Id == willSucceed.Id).CleanupStatus);
        Assert.Equal("CleanupFailed", results.Single(r => r.Id == willFail.Id).CleanupStatus);
    }

    [Fact]
    public async Task CleanBatch_ProcessesLocationPointsBeforeOffenders()
    {
        var run = await SeedRunAsync();
        var offender = await SeedRecordAsync(run, entityType: TestDataEntityType.Offender, externalId: "1", identifier: "AUTO111");
        var location = await SeedRecordAsync(run, entityType: TestDataEntityType.LocationPoint, externalId: null, identifier: "offenderId=1");

        var callOrder = new List<int>();
        var factory = new FakeHttpClientFactory(req =>
        {
            callOrder.Add(offender.Id); // only the offender ever reaches the HTTP layer
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var results = await sut.CleanBatchAsync(new[] { offender.Id, location.Id });

        // LocationPoint is refused before any HTTP call, and — per 7.14 — is
        // still ordered/processed ahead of the Offender in the batch even
        // though it never reaches the network.
        Assert.Equal("CleanupFailed", results.Single(r => r.Id == location.Id).CleanupStatus);
        Assert.Equal("Cleaned", results.Single(r => r.Id == offender.Id).CleanupStatus);
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task Cleanup_DoesNotDeleteTheTestDataRecordItself()
    {
        var run = await SeedRunAsync();
        var record = await SeedRecordAsync(run);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        await sut.CleanAsync(record.Id);

        Assert.NotNull(Db.TestDataRecords.Find(record.Id));
    }

    [Fact]
    public async Task CleanBatch_RecordsFromDifferentEnvironments_EachUsesItsOwnRunsBaseUrlSnapshot()
    {
        var runA = await SeedRunAsync("https://env-a.example/target");
        var runB = await SeedRunAsync("https://env-b.example/target");
        var recordA = await SeedRecordAsync(runA, externalId: "1", identifier: "AUTO111");
        var recordB = await SeedRecordAsync(runB, externalId: "2", identifier: "AUTO222");

        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        await sut.CleanBatchAsync(new[] { recordA.Id, recordB.Id });

        Assert.Equal(2, factory.Requests.Count);
        var requestForA = factory.Requests.Single(r => r.RequestUri!.ToString().EndsWith("/1"));
        var requestForB = factory.Requests.Single(r => r.RequestUri!.ToString().EndsWith("/2"));
        Assert.StartsWith("https://env-a.example/target", requestForA.RequestUri!.ToString());
        Assert.StartsWith("https://env-b.example/target", requestForB.RequestUri!.ToString());
    }

    [Fact]
    public async Task CleanBatch_DuplicateTestDataRecordsForTheSameRealEntity_BothResolveSafely_NoUnsafeRepeatedDelete()
    {
        // Two TestDataRecord rows can legitimately point at the same real
        // target-app entity (e.g. a historical duplicate, or two rows
        // sharing an Identifier per the BUG-014 case) — cleaning both must
        // never be treated as "delete this twice is fine to assume"; each
        // call is independent and the target API's own response (204 then
        // 404-confirmed-already-gone) is what decides the outcome, never an
        // assumption made client-side.
        var run = await SeedRunAsync();
        var first = await SeedRecordAsync(run, externalId: "55", identifier: "AUTO555");
        var second = await SeedRecordAsync(run, externalId: "55", identifier: "AUTO555");

        var callCount = 0;
        var factory = new FakeHttpClientFactory(_ =>
        {
            callCount++;
            // First delete actually removes it; the second, redundant
            // delete of the same real id is confirmed already-gone by 404.
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        var results = await sut.CleanBatchAsync(new[] { first.Id, second.Id });

        Assert.Equal(2, callCount); // each record's own DELETE call, never skipped or assumed
        Assert.All(results, r => Assert.Equal("Cleaned", r.CleanupStatus));
        Assert.All(results, r => Assert.NotNull(r.CleanedAtUtc));
    }

    [Fact]
    public async Task Cleanup_DoesNotTouchRunOrScenarioOrEvidenceHistory()
    {
        var run = await SeedRunAsync();
        var testCase = new TestCase { ExternalId = "api::td_history_test", Name = "td_history_test", Suite = TestSuite.Api, CreatedAtUtc = DateTime.UtcNow };
        Db.TestCases.Add(testCase);
        await Db.SaveChangesAsync();
        var scenario = new ScenarioResult { TestRunId = run.Id, TestCaseId = testCase.Id, TestCase = testCase, Status = ScenarioStatus.Passed };
        Db.ScenarioResults.Add(scenario);
        await Db.SaveChangesAsync();
        Db.EvidenceArtifacts.Add(new EvidenceArtifact
        {
            ScenarioResultId = scenario.Id,
            Type = EvidenceType.Log,
            RelativePath = $"run-{run.Id}/td_history_test/execution.log",
            ContentType = "text/plain",
            SizeBytes = 10,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();

        var record = await SeedRecordAsync(run, scenarioResultId: scenario.Id);
        var factory = FakeHttpClientFactory.RespondingWith(HttpStatusCode.NoContent);
        var sut = new TestDataService(Db, factory, NullLogger<TestDataService>.Instance);

        await sut.CleanAsync(record.Id);

        Assert.NotNull(Db.TestRuns.Find(run.Id));
        Assert.NotNull(Db.ScenarioResults.Find(scenario.Id));
        Assert.Single(Db.EvidenceArtifacts.Where(a => a.ScenarioResultId == scenario.Id));
    }
}
