namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// The stable identity of one automated test across every run that has ever
/// executed it (TM-04). The same row is reused run after run — there is
/// deliberately no separate "TestHistory" table; history is derived by
/// querying <see cref="ScenarioResult"/>s for a given TestCase in
/// chronological order.
/// </summary>
public class TestCase
{
    public int Id { get; set; }

    /// <summary>
    /// Required, unique. The stable identity a runner reports this test
    /// under — for pytest this will normally be the pytest nodeid (e.g.
    /// "test_api03_validation.py::test_create_offender_rejects_empty_last_name"),
    /// for Playwright the spec file + test title. Runner integration
    /// (Step 4) is what actually populates this; the column just needs to
    /// exist and be unique now.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TestSuite Suite { get; set; }

    /// <summary>Metadata only, e.g. "FR-01" or "API-03". Not a foreign key.</summary>
    public string? RequirementId { get; set; }

    /// <summary>
    /// Metadata only, e.g. "BUG-001". Single nullable field per the Step 2
    /// spec — a test that maps to more than one bug (e.g. fr10's
    /// [BUG-007 / BUG-018]) stores that as one string rather than a
    /// relationship, kept intentionally simple for this step.
    /// </summary>
    public string? BugId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ScenarioResult> ScenarioResults { get; set; } = new List<ScenarioResult>();
}
