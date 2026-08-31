using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.Hubs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>Which runner a given phase launches.</summary>
public enum RunnerKind
{
    Api,
    Ui,
}

/// <summary>
/// Owns one TestRun's entire execution (4.16). Scoped — a fresh instance
/// (with its own DbContext) is resolved per run by
/// <see cref="RunExecutionBackgroundService"/>; never shared across runs or
/// held for longer than one run's lifetime.
/// </summary>
public class RunOrchestrator
{
    private enum SuitePhaseOutcome
    {
        CompletedNormally,
        InfrastructureFailure,
        Cancelled,
    }

    private readonly TestManagementDbContext _db;
    private readonly RunnerOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IHubContext<RunHub> _hub;
    private readonly ILogger<RunOrchestrator> _logger;

    private int _runId;
    private readonly Dictionary<string, int> _testCaseIdByExternalId = new();

    public RunOrchestrator(
        TestManagementDbContext db,
        IOptions<RunnerOptions> options,
        IHostEnvironment hostEnvironment,
        IHubContext<RunHub> hub,
        ILogger<RunOrchestrator> logger)
    {
        _db = db;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _hub = hub;
        _logger = logger;
    }

    public async Task RunAsync(int runId, CancellationToken ct)
    {
        _runId = runId;

        var run = await _db.TestRuns.FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
        if (run is null || run.Status != RunStatus.Queued)
        {
            // Already handled — e.g. Stop flipped a still-Queued run to
            // Stopped before this worker got to it. Nothing to do.
            return;
        }

        run.Status = RunStatus.Running;
        run.StartedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        await BroadcastRunUpdatedAsync(run);

        // TM-08 (6.14) — the run's own artifact root, created once up front
        // so both suite phases can write evidence into it via
        // OFFENDERWATCH_ARTIFACT_DIR.
        Directory.CreateDirectory(RunArtifactRoot);

        var baseUrl = run.BaseUrlSnapshot;

        // 4.14 — sequential, intentional: pytest first, then Playwright.
        // Stop during pytest must prevent Playwright from ever starting.
        var apiOutcome = await RunSuiteAsync(RunnerKind.Api, baseUrl, ct);
        if (apiOutcome != SuitePhaseOutcome.CompletedNormally)
        {
            await FinalizeAsync(apiOutcome == SuitePhaseOutcome.Cancelled ? RunStatus.Stopped : RunStatus.Failed);
            return;
        }

        var uiOutcome = await RunSuiteAsync(RunnerKind.Ui, baseUrl, ct);
        if (uiOutcome != SuitePhaseOutcome.CompletedNormally)
        {
            await FinalizeAsync(uiOutcome == SuitePhaseOutcome.Cancelled ? RunStatus.Stopped : RunStatus.Failed);
            return;
        }

        await FinalizeAsync(RunStatus.Completed);
    }

    /// <summary>
    /// Test seam (4.25) — applies one already-parsed event through the exact
    /// same persistence path <see cref="RunSuiteAsync"/> uses for real
    /// runner output, without spawning any process. Lets backend tests
    /// exercise TestCase reuse / ScenarioResult creation / classification
    /// deterministically.
    /// </summary>
    public Task ApplyEventForTestingAsync(int runId, RunnerKind kind, OwEvent evt)
    {
        _runId = runId;
        return PersistEventAsync(kind, evt);
    }

    /// <summary>Test seam (4.25) — computes and persists final totals/status exactly as <see cref="RunAsync"/> does.</summary>
    public Task FinalizeForTestingAsync(int runId, RunStatus status)
    {
        _runId = runId;
        return FinalizeAsync(status);
    }

    private async Task<SuitePhaseOutcome> RunSuiteAsync(RunnerKind kind, string baseUrl, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return SuitePhaseOutcome.Cancelled;
        }

        var psi = BuildProcessStartInfo(kind, baseUrl);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var lines = Channel.CreateUnbounded<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lines.Writer.TryWrite(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _logger.LogDebug("[run {RunId} / {Kind} stderr] {Line}", _runId, kind, e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                _logger.LogError("Run {RunId}: {Kind} runner process.Start() returned false", _runId, kind);
                return SuitePhaseOutcome.InfrastructureFailure;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId}: failed to start {Kind} runner ({FileName})", _runId, kind, psi.FileName);
            return SuitePhaseOutcome.InfrastructureFailure;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var sawSuiteFinished = false;

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var line in lines.Reader.ReadAllAsync(CancellationToken.None))
            {
                if (!OwEventParser.TryParse(line, out var evt) || evt is null)
                {
                    continue; // ordinary runner console output — ignored (4.7)
                }
                if (evt.EventType == "suite_finished")
                {
                    sawSuiteFinished = true;
                }
                await PersistEventAsync(kind, evt);
            }
        });

        var waitForExit = process.WaitForExitAsync(CancellationToken.None);
        var cancelSignal = ct.CanBeCanceled ? Task.Delay(Timeout.Infinite, ct) : null;

        var completedTask = cancelSignal is null
            ? await Task.WhenAny(waitForExit)
            : await Task.WhenAny(waitForExit, cancelSignal);

        var wasCancelled = cancelSignal is not null && completedTask == cancelSignal;
        if (wasCancelled)
        {
            TryKillProcessTree(process, kind);
            await waitForExit; // wait for the OS to actually finish tearing it down
        }

        lines.Writer.TryComplete();
        await consumerTask;

        if (wasCancelled)
        {
            await CancelPendingScenariosAsync();
            return SuitePhaseOutcome.Cancelled;
        }

        if (!sawSuiteFinished)
        {
            _logger.LogError(
                "Run {RunId}: {Kind} runner exited (code {ExitCode}) without a suite_finished event — treating as infrastructure failure",
                _runId, kind, process.ExitCode);
            return SuitePhaseOutcome.InfrastructureFailure;
        }

        // 4.21 — a non-zero exit code from real test failures is expected
        // and NOT an infrastructure failure; the structured lifecycle
        // (sawSuiteFinished) is what matters, not process.ExitCode.
        return SuitePhaseOutcome.CompletedNormally;
    }

    private ProcessStartInfo BuildProcessStartInfo(RunnerKind kind, string baseUrl)
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(_hostEnvironment.ContentRootPath, _options.RepoRootRelativeToContentRoot));

        ProcessStartInfo psi = kind switch
        {
            RunnerKind.Api => new ProcessStartInfo
            {
                FileName = _options.PythonExecutable,
                WorkingDirectory = Path.Combine(repoRoot, ToPath(_options.PytestWorkingDirectory)),
            },
            RunnerKind.Ui => new ProcessStartInfo
            {
                // PlaywrightExecutableRelativePath is relative to the UI
                // suite's own working directory (it's node_modules/.bin/...
                // inside automation/ui), not the repo root.
                FileName = Path.Combine(
                    repoRoot, ToPath(_options.PlaywrightWorkingDirectory), ToPath(_options.PlaywrightExecutableRelativePath)),
                WorkingDirectory = Path.Combine(repoRoot, ToPath(_options.PlaywrightWorkingDirectory)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var args = kind == RunnerKind.Api ? _options.PytestArguments : _options.PlaywrightArguments;
        foreach (var arg in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            psi.ArgumentList.Add(arg);
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        // The immutable Run snapshot, not a live re-read of the Environment
        // record (4.15) — the child process only ever sees what was frozen
        // onto this TestRun at creation time.
        psi.EnvironmentVariables["OFFENDERWATCH_BASE_URL"] = baseUrl;
        // TM-08 (6.14) — where this run's evidence must be written. The
        // runner writes only beneath this directory; the orchestrator
        // rejects anything else at ingestion time (HandleArtifactCreatedAsync).
        psi.EnvironmentVariables["OFFENDERWATCH_ARTIFACT_DIR"] = RunArtifactRoot;

        return psi;
    }

    private static string ToPath(string configuredRelativePath) =>
        configuredRelativePath.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>TM-08 (6.9) — test-management/artifacts/, resolved the same way every other configured path is (never a hard-coded absolute path).</summary>
    private string ArtifactRoot => Path.GetFullPath(
        Path.Combine(_hostEnvironment.ContentRootPath, _options.ArtifactRootRelativeToContentRoot));

    /// <summary>TM-08 (6.14) — this run's own artifact subdirectory, e.g. artifacts/run-123/.</summary>
    private string RunArtifactRoot => Path.Combine(ArtifactRoot, $"run-{_runId}");

    private void TryKillProcessTree(Process process, RunnerKind kind)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId}: failed to kill {Kind} process tree", _runId, kind);
        }
    }

    // ---- event persistence --------------------------------------------

    private async Task PersistEventAsync(RunnerKind kind, OwEvent evt)
    {
        switch (evt.EventType)
        {
            case "scenario_discovered":
                await HandleDiscoveredAsync(kind, evt);
                break;
            case "scenario_started":
                await HandleStartedAsync(evt);
                break;
            case "scenario_finished":
                await HandleFinishedAsync(evt);
                break;
            case "artifact_created":
                await HandleArtifactCreatedAsync(evt);
                break;
            // "suite_finished" carries no per-scenario state to persist;
            // the caller already recorded that it was observed.
        }
    }

    private async Task HandleDiscoveredAsync(RunnerKind kind, OwEvent evt)
    {
        if (evt.ExternalId is null)
        {
            return;
        }

        var suite = kind == RunnerKind.Api ? TestSuite.Api : TestSuite.Ui;

        var testCase = await _db.TestCases.FirstOrDefaultAsync(t => t.ExternalId == evt.ExternalId, CancellationToken.None);
        if (testCase is null)
        {
            // 4.8 — reused by this same stable ExternalId in every future run.
            testCase = new TestCase
            {
                ExternalId = evt.ExternalId,
                Name = evt.Name ?? evt.ExternalId,
                Suite = suite,
                RequirementId = evt.RequirementId,
                BugId = evt.BugId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.TestCases.Add(testCase);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        else
        {
            // 4.12.3 — reuse the same row; only refresh conservative,
            // non-historical descriptive metadata when this run's runner
            // actually reports a (non-null) value for it.
            var changed = false;
            if (evt.Name is not null && testCase.Name != evt.Name)
            {
                testCase.Name = evt.Name;
                changed = true;
            }
            if (evt.RequirementId is not null && testCase.RequirementId != evt.RequirementId)
            {
                testCase.RequirementId = evt.RequirementId;
                changed = true;
            }
            if (evt.BugId is not null && testCase.BugId != evt.BugId)
            {
                testCase.BugId = evt.BugId;
                changed = true;
            }
            if (changed)
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }

        _testCaseIdByExternalId[evt.ExternalId] = testCase.Id;

        // Respect the (TestRunId, TestCaseId) unique constraint even if a
        // discovered event were ever somehow delivered twice.
        var alreadyExists = await _db.ScenarioResults
            .AnyAsync(sr => sr.TestRunId == _runId && sr.TestCaseId == testCase.Id, CancellationToken.None);
        if (!alreadyExists)
        {
            var scenarioResult = new ScenarioResult
            {
                TestRunId = _runId,
                TestCaseId = testCase.Id,
                TestCase = testCase,
                Status = ScenarioStatus.Queued,
            };
            _db.ScenarioResults.Add(scenarioResult);
            await _db.SaveChangesAsync(CancellationToken.None);
            // 5.4 — scenario creation as Queued, useful so the UI can show
            // the full scenario list before anything has started running.
            await BroadcastScenarioUpdatedAsync(scenarioResult);
        }
    }

    private async Task HandleStartedAsync(OwEvent evt)
    {
        var result = await FindScenarioResultAsync(evt.ExternalId);
        if (result is null)
        {
            return;
        }
        result.Status = ScenarioStatus.Running;
        result.StartedAtUtc = evt.TimestampUtc;
        await _db.SaveChangesAsync(CancellationToken.None);
        await BroadcastScenarioUpdatedAsync(result);
    }

    private async Task HandleFinishedAsync(OwEvent evt)
    {
        if (evt.ExternalId is null || !_testCaseIdByExternalId.TryGetValue(evt.ExternalId, out var testCaseId))
        {
            return;
        }

        var result = await FindScenarioResultAsync(evt.ExternalId);
        if (result is null)
        {
            return;
        }

        var testCase = await _db.TestCases.FindAsync(new object?[] { testCaseId }, CancellationToken.None);
        var hasKnownDefectMetadata = !string.IsNullOrWhiteSpace(testCase?.BugId);

        result.Status = ScenarioClassifier.ClassifyFinalStatus(evt.Status, evt.NativeExpectedFailure == true, hasKnownDefectMetadata);
        result.EndedAtUtc = evt.TimestampUtc;
        result.DurationMs = evt.DurationMs.HasValue ? (int)Math.Min(evt.DurationMs.Value, int.MaxValue) : null;
        result.FailureMessage = evt.FailureMessage;
        result.StackTrace = evt.StackTrace;

        await _db.SaveChangesAsync(CancellationToken.None);
        await BroadcastScenarioUpdatedAsync(result);
    }

    /// <summary>
    /// TM-08 (6.13/6.14) — registers evidence a runner already wrote to
    /// disk. Never trusts the reported path blindly: it must resolve
    /// (after combining with this run's own artifact root) to a real file
    /// strictly inside that root, or the event is logged and dropped —
    /// exactly the same "ignore what can't be trusted" discipline
    /// <see cref="OwEventParser"/> already applies to malformed JSON.
    /// </summary>
    private async Task HandleArtifactCreatedAsync(OwEvent evt)
    {
        if (evt.ExternalId is null || evt.Path is null || evt.ArtifactType is null)
        {
            return;
        }

        if (!Enum.TryParse<EvidenceType>(evt.ArtifactType, ignoreCase: true, out var type))
        {
            _logger.LogWarning("Run {RunId}: artifact_created had an unrecognized artifactType '{ArtifactType}' from {ExternalId}", _runId, evt.ArtifactType, evt.ExternalId);
            return;
        }

        var scenarioResult = await FindScenarioResultAsync(evt.ExternalId);
        if (scenarioResult is null)
        {
            _logger.LogWarning("Run {RunId}: artifact_created referenced an unknown scenario {ExternalId}", _runId, evt.ExternalId);
            return;
        }

        var runRoot = Path.GetFullPath(RunArtifactRoot);
        var candidate = Path.GetFullPath(Path.Combine(runRoot, ToPath(evt.Path)));
        var isInsideRunRoot = candidate.StartsWith(runRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!isInsideRunRoot)
        {
            _logger.LogWarning("Run {RunId}: rejected artifact path outside this run's artifact directory: '{Path}'", _runId, evt.Path);
            return;
        }
        if (!File.Exists(candidate))
        {
            _logger.LogWarning("Run {RunId}: artifact_created referenced a file that does not exist: '{Path}'", _runId, evt.Path);
            return;
        }

        var relativeToArtifactRoot = Path.GetRelativePath(ArtifactRoot, candidate).Replace(Path.DirectorySeparatorChar, '/');
        var sizeBytes = new FileInfo(candidate).Length;

        // 6.10 — a fresh row every time, never an update of an existing one:
        // historical evidence for an already-finalized ScenarioResult (a
        // different run's row for the same TestCase) is never touched here,
        // since this always targets *this* run's own ScenarioResult.
        _db.EvidenceArtifacts.Add(new EvidenceArtifact
        {
            ScenarioResultId = scenarioResult.Id,
            Type = type,
            RelativePath = relativeToArtifactRoot,
            ContentType = string.IsNullOrWhiteSpace(evt.ContentType) ? GuessContentType(candidate) : evt.ContentType,
            SizeBytes = sizeBytes,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static string GuessContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".json" => "application/json",
        ".zip" => "application/zip",
        ".log" or ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    private Task<ScenarioResult?> FindScenarioResultAsync(string? externalId)
    {
        if (externalId is null || !_testCaseIdByExternalId.TryGetValue(externalId, out var testCaseId))
        {
            return Task.FromResult<ScenarioResult?>(null);
        }
        return _db.ScenarioResults
            .Include(sr => sr.TestCase)
            .FirstOrDefaultAsync(sr => sr.TestRunId == _runId && sr.TestCaseId == testCaseId, CancellationToken.None)!;
    }

    private async Task CancelPendingScenariosAsync()
    {
        var pending = await _db.ScenarioResults
            .Include(sr => sr.TestCase)
            .Where(sr => sr.TestRunId == _runId
                && (sr.Status == ScenarioStatus.Queued || sr.Status == ScenarioStatus.Running))
            .ToListAsync(CancellationToken.None);

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var sr in pending)
        {
            sr.Status = ScenarioStatus.Cancelled;
            sr.EndedAtUtc = now;
        }
        await _db.SaveChangesAsync(CancellationToken.None);

        // 5.6 — Cancelled scenario updates broadcast before the final
        // RunUpdated(Stopped) that FinalizeAsync sends right after this.
        foreach (var sr in pending)
        {
            await BroadcastScenarioUpdatedAsync(sr);
        }
    }

    private async Task FinalizeAsync(RunStatus status)
    {
        var run = await _db.TestRuns.FirstAsync(r => r.Id == _runId, CancellationToken.None);

        var counts = await _db.ScenarioResults
            .Where(sr => sr.TestRunId == _runId)
            .GroupBy(sr => sr.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(CancellationToken.None);

        int CountOf(ScenarioStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;

        // 4.17 — always derived from persisted ScenarioResults, never from
        // a process exit code. Cancelled scenarios are simply not counted
        // in any of these four fields.
        run.PassedCount = CountOf(ScenarioStatus.Passed);
        run.FailedCount = CountOf(ScenarioStatus.Failed);
        run.ExpectedFailedCount = CountOf(ScenarioStatus.ExpectedFail);
        run.SkippedCount = CountOf(ScenarioStatus.Skipped);

        run.Status = status;
        run.EndedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(CancellationToken.None);
        // 5.4 — final RunUpdated: Running -> Completed/Failed, or -> Stopped.
        await BroadcastRunUpdatedAsync(run);
    }

    // ---- SignalR broadcast (Step 5 / TM-03) ----------------------------
    //
    // Always called *after* the corresponding SaveChangesAsync above (5.4) —
    // SQLite is the source of truth, SignalR is purely a notification on
    // top of an already-committed change. Broadcasting is wrapped so a
    // transport failure (e.g. no clients connected, a serialization hiccup)
    // can never crash the run or affect its persisted status (5.5).

    private Task BroadcastRunUpdatedAsync(TestRun run) =>
        SafeBroadcastAsync("RunUpdated", RunDtoMapper.ToSummaryDto(run));

    private Task BroadcastScenarioUpdatedAsync(ScenarioResult scenarioResult) =>
        SafeBroadcastAsync("ScenarioUpdated", RunDtoMapper.ToScenarioResultDto(scenarioResult));

    private async Task SafeBroadcastAsync(string method, object payload)
    {
        try
        {
            await _hub.Clients.Group(RunHub.GroupName(_runId)).SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId}: SignalR broadcast of {Method} failed (execution continues unaffected)", _runId, method);
        }
    }
}
