using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Data;
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
    private readonly ILogger<RunOrchestrator> _logger;

    private int _runId;
    private readonly Dictionary<string, int> _testCaseIdByExternalId = new();

    public RunOrchestrator(
        TestManagementDbContext db,
        IOptions<RunnerOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<RunOrchestrator> logger)
    {
        _db = db;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
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

        return psi;
    }

    private static string ToPath(string configuredRelativePath) =>
        configuredRelativePath.Replace('/', Path.DirectorySeparatorChar);

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
            _db.ScenarioResults.Add(new ScenarioResult
            {
                TestRunId = _runId,
                TestCaseId = testCase.Id,
                Status = ScenarioStatus.Queued,
            });
            await _db.SaveChangesAsync(CancellationToken.None);
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
    }

    private Task<ScenarioResult?> FindScenarioResultAsync(string? externalId)
    {
        if (externalId is null || !_testCaseIdByExternalId.TryGetValue(externalId, out var testCaseId))
        {
            return Task.FromResult<ScenarioResult?>(null);
        }
        return _db.ScenarioResults
            .FirstOrDefaultAsync(sr => sr.TestRunId == _runId && sr.TestCaseId == testCaseId, CancellationToken.None)!;
    }

    private async Task CancelPendingScenariosAsync()
    {
        var pending = await _db.ScenarioResults
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
    }
}
