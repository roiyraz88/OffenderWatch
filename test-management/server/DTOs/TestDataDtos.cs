namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>TM-06 (7.13) — never an EF entity; never a raw filesystem/DB detail beyond what the UI needs.</summary>
public class TestDataRecordDto
{
    public int Id { get; set; }
    public int TestRunId { get; set; }
    public int? ScenarioResultId { get; set; }
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Identifier { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CleanedAtUtc { get; set; }
    public string CleanupStatus { get; set; } = string.Empty;
}

/// <summary>POST /api/test-data/clean body — an explicit list only; an empty list is rejected, never "clean everything" (7.13).</summary>
public class CleanTestDataBatchRequest
{
    public List<int> Ids { get; set; } = new();
}
