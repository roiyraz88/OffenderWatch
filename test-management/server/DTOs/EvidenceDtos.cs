namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>
/// TM-08 (6.18) — evidence metadata only. Never the filesystem path itself;
/// the browser fetches actual bytes through GET /api/evidence/{id}/content
/// (which resolves the path server-side and validates it).
/// </summary>
public class EvidenceArtifactDto
{
    public int Id { get; set; }
    public int ScenarioResultId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
