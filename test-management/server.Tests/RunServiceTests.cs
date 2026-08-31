using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;
using EnvironmentEntity = OffenderWatch.TestManagement.Server.Models.Environment;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Step 4.25 — RunService's own HTTP-facing rules (create/read/stop),
/// deterministic and independent of any real runner process. The
/// background worker is deliberately never started here — RunQueue is just
/// drained/inspected directly, and RunOrchestrator's own execution is
/// covered separately (RunOrchestratorPersistenceTests).
/// </summary>
public class RunServiceTests : TestDatabaseFixture
{
    private readonly RunQueue _queue = new();
    private readonly RunCancellationRegistry _cancellation = new();
    private readonly RunService _sut;

    public RunServiceTests()
    {
        _sut = new RunService(Db, _queue, _cancellation);
    }

    private async Task<EnvironmentEntity> SeedEnvironmentAsync(string name = "Dev")
    {
        var env = new EnvironmentEntity
        {
            Name = name,
            BaseUrl = "https://example.com/target",
            IsDefault = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        Db.Environments.Add(env);
        await Db.SaveChangesAsync();
        return env;
    }

    [Fact]
    public async Task CreateAsync_SnapshotsEnvironmentNameAndBaseUrl()
    {
        var env = await SeedEnvironmentAsync("Staging");

        var run = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });

        Assert.Equal("Staging", run.EnvironmentNameSnapshot);
        Assert.Equal("https://example.com/target", run.BaseUrlSnapshot);
        Assert.Equal(env.Id, run.EnvironmentId);
    }

    [Fact]
    public async Task CreateAsync_MissingEnvironment_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EnvironmentNotFoundException>(
            () => _sut.CreateAsync(new CreateRunRequest { EnvironmentId = 999 }));
    }

    [Fact]
    public async Task CreateAsync_NewRun_StartsQueued()
    {
        var env = await SeedEnvironmentAsync();

        var run = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });

        Assert.Equal(nameof(RunStatus.Queued), run.Status);
        Assert.Null(run.StartedAtUtc);
        Assert.Null(run.EndedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_EnqueuesTheNewRunId()
    {
        var env = await SeedEnvironmentAsync();

        var run = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });

        var dequeued = await _queue.Reader.ReadAsync();
        Assert.Equal(run.Id, dequeued);
    }

    [Fact]
    public async Task StopAsync_OnQueuedRun_MarksStoppedDirectlyWithoutStartingIt()
    {
        var env = await SeedEnvironmentAsync();
        var run = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });

        await _sut.StopAsync(run.Id);

        var reloaded = await _sut.GetByIdAsync(run.Id);
        Assert.Equal(nameof(RunStatus.Stopped), reloaded.Status);
        Assert.NotNull(reloaded.EndedAtUtc);
    }

    [Fact]
    public async Task StopAsync_OnAlreadyFinishedRun_ThrowsConflict()
    {
        var env = await SeedEnvironmentAsync();
        var run = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });
        await _sut.StopAsync(run.Id); // Queued -> Stopped

        await Assert.ThrowsAsync<RunConflictException>(() => _sut.StopAsync(run.Id));
    }

    [Fact]
    public async Task StopAsync_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<RunNotFoundException>(() => _sut.StopAsync(999));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<RunNotFoundException>(() => _sut.GetByIdAsync(999));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNewestFirst()
    {
        var env = await SeedEnvironmentAsync();
        var first = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });
        var second = await _sut.CreateAsync(new CreateRunRequest { EnvironmentId = env.Id });

        var all = await _sut.GetAllAsync();

        Assert.Equal(second.Id, all[0].Id);
        Assert.Equal(first.Id, all[1].Id);
    }
}
