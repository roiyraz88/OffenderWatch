namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// The result of one <see cref="TestCase"/> in one <see cref="TestRun"/> —
/// the append-only historical execution record. A (TestRunId, TestCaseId)
/// pair is unique: one TestCase has at most one ScenarioResult per run.
/// A completed run's ScenarioResults are never edited by a later run; a
/// later run creates its own new ScenarioResult rows instead.
/// </summary>
public class ScenarioResult
{
    public int Id { get; set; }

    public int TestRunId { get; set; }

    public TestRun TestRun { get; set; } = null!;

    public int TestCaseId { get; set; }

    public TestCase TestCase { get; set; } = null!;

    /// <summary>
    /// Queued -> Running -> a final status, while the owning run is active.
    /// This is the one place a row is mutated in place — it does not
    /// violate append-only history, since a *different* run's execution of
    /// the same TestCase is always a brand new row, never an edit of this one.
    /// </summary>
    public ScenarioStatus Status { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public int? DurationMs { get; set; }

    /// <summary>Set for Failed/ExpectedFail results.</summary>
    public string? FailureMessage { get; set; }

    /// <summary>Set for Failed/ExpectedFail results, when available.</summary>
    public string? StackTrace { get; set; }

    public ICollection<EvidenceArtifact> EvidenceArtifacts { get; set; } = new List<EvidenceArtifact>();

    public ICollection<TestDataRecord> TestDataRecords { get; set; } = new List<TestDataRecord>();
}
