namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// A target OffenderWatch environment a run can execute against (TM-01).
/// Deleting or editing an Environment must never change the historical
/// record of a past run — see <see cref="TestRun.EnvironmentNameSnapshot"/>
/// and <see cref="TestRun.BaseUrlSnapshot"/>.
/// </summary>
public class Environment
{
    public int Id { get; set; }

    /// <summary>Required, unique (e.g. "Dev", "Staging", "Roie").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Required. The OffenderWatch base URL for this environment — never
    /// hard-coded elsewhere in the platform or the suite (TM-01).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// At most one Environment may have this set. Enforcing that invariant
    /// is the Environment service/API's job (Step 3), not the data model's.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<TestRun> TestRuns { get; set; } = new List<TestRun>();
}
