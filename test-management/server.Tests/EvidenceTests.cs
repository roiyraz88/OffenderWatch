using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Controllers;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>Step 6.25 — TM-08 evidence ingestion (RunOrchestrator.HandleArtifactCreatedAsync, via its test seam) and safe content retrieval (EvidenceController), against a temporary artifact directory — never test-management/artifacts/ itself.</summary>
public class EvidenceTests : TestDatabaseFixture, IDisposable
{
    private readonly string _contentRoot;
    private readonly RunnerOptions _options = new() { ArtifactRootRelativeToContentRoot = "artifacts" };
    private readonly RunOrchestrator _sut;

    public EvidenceTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"tm-evidence-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        _sut = new RunOrchestrator(
            Db,
            Options.Create(_options),
            new StubHostEnvironment(_contentRoot),
            TestHubContext.Real(),
            NullLogger<RunOrchestrator>.Instance);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private string ArtifactRoot => Path.Combine(_contentRoot, "artifacts");

    private async Task<(TestRun run, TestCase testCase, ScenarioResult scenario)> SeedDiscoveredScenarioAsync(string externalId)
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

        var discovered = new OwEvent
        {
            Version = 1,
            EventType = "scenario_discovered",
            Runner = "pytest",
            TimestampUtc = DateTime.UtcNow,
            ExternalId = externalId,
            Name = externalId,
            Suite = "API",
        };
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, discovered);

        var testCase = Db.TestCases.Single(t => t.ExternalId == externalId);
        var scenario = Db.ScenarioResults.Single(sr => sr.TestRunId == run.Id && sr.TestCaseId == testCase.Id);
        return (run, testCase, scenario);
    }

    private string WriteRunArtifactFile(int runId, string relativePath, string content = "log contents")
    {
        var full = Path.Combine(ArtifactRoot, $"run-{runId}", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private OwEvent ArtifactCreated(string externalId, string artifactType, string path, string? contentType = null) => new()
    {
        Version = 1,
        EventType = "artifact_created",
        Runner = "pytest",
        TimestampUtc = DateTime.UtcNow,
        ExternalId = externalId,
        ArtifactType = artifactType,
        Path = path,
        ContentType = contentType,
    };

    [Fact]
    public async Task ArtifactCreated_RegistersEvidenceForTheExactScenarioResult()
    {
        var (run, _, scenario) = await SeedDiscoveredScenarioAsync("api::ev_test");
        WriteRunArtifactFile(run.Id, "ev_test/execution.log");

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::ev_test", "Log", "ev_test/execution.log", "text/plain"));

        var artifact = Db.EvidenceArtifacts.Single(a => a.ScenarioResultId == scenario.Id);
        Assert.Equal(EvidenceType.Log, artifact.Type);
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.True(artifact.SizeBytes > 0);
        Assert.Equal($"run-{run.Id}/ev_test/execution.log", artifact.RelativePath);
    }

    [Fact]
    public async Task ArtifactCreated_UnknownScenario_IsIgnoredNotThrown()
    {
        // No scenario_discovered was ever applied for this run/externalId.
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

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::never_discovered", "Log", "x/execution.log"));

        Assert.Empty(Db.EvidenceArtifacts);
    }

    [Fact]
    public async Task ArtifactCreated_PathTraversal_IsRejected()
    {
        var (run, _, scenario) = await SeedDiscoveredScenarioAsync("api::traversal_test");
        // A real file that exists, but reached via a traversal path outside this run's own artifact directory.
        Directory.CreateDirectory(ArtifactRoot);
        var outside = Path.Combine(ArtifactRoot, "secret.txt");
        File.WriteAllText(outside, "should never be exposed");

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::traversal_test", "Log", "../secret.txt"));

        Assert.Empty(Db.EvidenceArtifacts.Where(a => a.ScenarioResultId == scenario.Id));
    }

    [Fact]
    public async Task ArtifactCreated_ReferencedFileMissing_IsIgnoredNotThrown()
    {
        var (run, _, scenario) = await SeedDiscoveredScenarioAsync("api::missing_file_test");

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::missing_file_test", "Log", "missing_file_test/does_not_exist.log"));

        Assert.Empty(Db.EvidenceArtifacts.Where(a => a.ScenarioResultId == scenario.Id));
    }

    [Fact]
    public async Task ArtifactCreated_UnknownArtifactType_IsIgnoredNotThrown()
    {
        var (run, _, scenario) = await SeedDiscoveredScenarioAsync("api::bad_type_test");
        WriteRunArtifactFile(run.Id, "bad_type_test/thing.bin");

        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::bad_type_test", "NotARealType", "bad_type_test/thing.bin"));

        Assert.Empty(Db.EvidenceArtifacts.Where(a => a.ScenarioResultId == scenario.Id));
    }

    [Fact]
    public async Task OlderRunEvidence_IsNotReplacedByALaterRunOfTheSameTestCase()
    {
        var (run1, _, scenario1) = await SeedDiscoveredScenarioAsync("api::same_test_across_runs");
        WriteRunArtifactFile(run1.Id, "same_test_across_runs/execution.log", "run 1 log");
        await _sut.ApplyEventForTestingAsync(run1.Id, RunnerKind.Api, ArtifactCreated("api::same_test_across_runs", "Log", "same_test_across_runs/execution.log"));

        var (run2, _, scenario2) = await SeedDiscoveredScenarioAsync("api::same_test_across_runs");
        WriteRunArtifactFile(run2.Id, "same_test_across_runs/execution.log", "run 2 log");
        await _sut.ApplyEventForTestingAsync(run2.Id, RunnerKind.Api, ArtifactCreated("api::same_test_across_runs", "Log", "same_test_across_runs/execution.log"));

        var run1Artifact = Db.EvidenceArtifacts.Single(a => a.ScenarioResultId == scenario1.Id);
        var run2Artifact = Db.EvidenceArtifacts.Single(a => a.ScenarioResultId == scenario2.Id);

        Assert.NotEqual(run1Artifact.RelativePath, run2Artifact.RelativePath);
        Assert.Equal("run 1 log", File.ReadAllText(Path.Combine(ArtifactRoot, run1Artifact.RelativePath)));
        Assert.Equal("run 2 log", File.ReadAllText(Path.Combine(ArtifactRoot, run2Artifact.RelativePath)));
    }

    [Fact]
    public async Task EvidenceController_GetContent_ReturnsFileForValidArtifact()
    {
        var (run, _, scenario) = await SeedDiscoveredScenarioAsync("api::content_test");
        WriteRunArtifactFile(run.Id, "content_test/execution.log", "hello evidence");
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::content_test", "Log", "content_test/execution.log", "text/plain"));
        var artifact = Db.EvidenceArtifacts.Single(a => a.ScenarioResultId == scenario.Id);

        var controller = new EvidenceController(Db, Options.Create(_options), new StubHostEnvironment(_contentRoot));
        var result = await controller.GetContent(artifact.Id, CancellationToken.None);

        var fileResult = Assert.IsType<Microsoft.AspNetCore.Mvc.FileStreamResult>(result);
        Assert.Equal("text/plain", fileResult.ContentType);
        using var reader = new StreamReader(fileResult.FileStream);
        Assert.Equal("hello evidence", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task EvidenceController_UnknownId_Returns404()
    {
        var controller = new EvidenceController(Db, Options.Create(_options), new StubHostEnvironment(_contentRoot));
        var result = await controller.GetContent(999, CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    [Fact]
    public async Task CancellingAScenario_DoesNotRemoveEvidenceAlreadyRegisteredForACompletedOne()
    {
        var (run, _, completedScenario) = await SeedDiscoveredScenarioAsync("api::completed_with_evidence");
        WriteRunArtifactFile(run.Id, "completed_with_evidence/execution.log");
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, ArtifactCreated("api::completed_with_evidence", "Log", "completed_with_evidence/execution.log"));
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, new OwEvent
        {
            Version = 1,
            EventType = "scenario_finished",
            Runner = "pytest",
            TimestampUtc = DateTime.UtcNow,
            ExternalId = "api::completed_with_evidence",
            Status = "passed",
        });

        // A second scenario in the same run that never got evidence (simulating cancellation before evidence was produced).
        var discoveredOther = new OwEvent
        {
            Version = 1,
            EventType = "scenario_discovered",
            Runner = "pytest",
            TimestampUtc = DateTime.UtcNow,
            ExternalId = "api::cancelled_without_evidence",
            Name = "api::cancelled_without_evidence",
            Suite = "API",
        };
        await _sut.ApplyEventForTestingAsync(run.Id, RunnerKind.Api, discoveredOther);

        // Evidence for the completed scenario must still be exactly one row, untouched.
        Assert.Single(Db.EvidenceArtifacts.Where(a => a.ScenarioResultId == completedScenario.Id));
    }

    private class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "OffenderWatch.TestManagement.Server.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
