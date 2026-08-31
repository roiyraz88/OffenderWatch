namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>POST /api/runs body. BaseUrl is never accepted here — the selected Environment is the only source of the target (4.3).</summary>
public class CreateRunRequest
{
    public int EnvironmentId { get; set; }
}

public class RunSummaryDto
{
    public int Id { get; set; }
    public int? EnvironmentId { get; set; }
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;
    public string BaseUrlSnapshot { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>Computed (EndedAtUtc - StartedAtUtc); not a persisted column (4.18).</summary>
    public double? DurationSeconds { get; set; }

    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int ExpectedFailedCount { get; set; }
    public int SkippedCount { get; set; }
}

public class ScenarioResultDto
{
    public int Id { get; set; }
    public int TestCaseId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Suite { get; set; } = string.Empty;
    public string? RequirementId { get; set; }
    public string? BugId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public int? DurationMs { get; set; }
    public string? FailureMessage { get; set; }
    public string? StackTrace { get; set; }
}

public class RunDetailDto : RunSummaryDto
{
    public List<ScenarioResultDto> ScenarioResults { get; set; } = new();
}
