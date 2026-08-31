namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// One execution of the automation suite (TM-02, TM-05). History is
/// append-only: a new TestRun and its ScenarioResults are never used to
/// overwrite an earlier run's records.
/// </summary>
public class TestRun
{
    public int Id { get; set; }

    /// <summary>
    /// Nullable so the run survives if its Environment is later deleted —
    /// the historical target is preserved via the two snapshot fields below
    /// regardless of what happens to the Environment record.
    /// </summary>
    public int? EnvironmentId { get; set; }

    public Environment? Environment { get; set; }

    /// <summary>
    /// Immutable snapshot of the Environment's name at the moment this run
    /// was created. Never updated if the Environment is later renamed.
    /// </summary>
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;

    /// <summary>
    /// Immutable snapshot of the Environment's base URL at the moment this
    /// run was created. Never updated if the Environment is later edited.
    /// </summary>
    public string BaseUrlSnapshot { get; set; } = string.Empty;

    public RunStatus Status { get; set; }

    public RunTrigger Trigger { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Run-summary snapshot totals. Populated when the run's execution
    /// completes (Step 4+) — left at 0 by whatever creates the Queued row.
    /// </summary>
    public int PassedCount { get; set; }

    public int FailedCount { get; set; }

    public int ExpectedFailedCount { get; set; }

    public int SkippedCount { get; set; }

    public ICollection<ScenarioResult> ScenarioResults { get; set; } = new List<ScenarioResult>();

    public ICollection<TestDataRecord> TestDataRecords { get; set; } = new List<TestDataRecord>();
}
