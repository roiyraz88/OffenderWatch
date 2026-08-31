namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>
/// Immutable evidence belonging to one <see cref="ScenarioResult"/> (TM-08).
/// The binary itself lives on disk under test-management/artifacts/{runId}/
/// {scenarioResultId}/ — this row stores only metadata and the relative
/// path, so SQLite stays small and artifacts stay easy to inspect directly.
/// Writing/serving artifacts is implemented later (Step 6); this step only
/// defines where their metadata is recorded.
/// </summary>
public class EvidenceArtifact
{
    public int Id { get; set; }

    public int ScenarioResultId { get; set; }

    public ScenarioResult ScenarioResult { get; set; } = null!;

    public EvidenceType Type { get; set; }

    /// <summary>Path relative to test-management/artifacts/, e.g. "42/107/screenshot.png".</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
