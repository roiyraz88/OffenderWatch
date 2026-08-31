namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// Application data (an offender or location point) created by a scenario
/// during a run, tracked here so a later 'Clean test data' action (TM-06)
/// knows exactly what it's allowed to delete from the target environment.
/// The actual cleanup operation is implemented later — this step only
/// defines the record of ownership.
///
/// SAFETY: the original seeded OffenderWatch data (the 11 seed offenders
/// and their trails) must never be represented here, and future cleanup
/// logic must only ever act on rows that exist in this table — never on
/// data inferred by convention alone. The existing "AUTO" nationalId
/// prefix from Parts 3/4's automation remains a useful *additional* guard,
/// not the sole ownership mechanism.
/// </summary>
public class TestDataRecord
{
    public int Id { get; set; }

    public int TestRunId { get; set; }

    public TestRun TestRun { get; set; } = null!;

    /// <summary>Which scenario created this data, if known.</summary>
    public int? ScenarioResultId { get; set; }

    public ScenarioResult? ScenarioResult { get; set; }

    public TestDataEntityType EntityType { get; set; }

    /// <summary>The OffenderWatch application's own id for this record (e.g. an offender id), if known.</summary>
    public string? ExternalId { get; set; }

    /// <summary>A human-readable identifier, e.g. the AUTO-prefixed nationalId used to create it.</summary>
    public string? Identifier { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CleanedAtUtc { get; set; }

    public TestDataCleanupStatus CleanupStatus { get; set; } = TestDataCleanupStatus.Active;
}
