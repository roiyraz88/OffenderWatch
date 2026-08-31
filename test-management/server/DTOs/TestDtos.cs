namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>TM-04 (6.1) — one row of GET /api/tests. Everything here is derived from ScenarioResults, never duplicated/persisted.</summary>
public class TestCaseSummaryDto
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Suite { get; set; } = string.Empty;
    public string? RequirementId { get; set; }
    public string? BugId { get; set; }

    public string? LastStatus { get; set; }
    public int? LastRunId { get; set; }
    public DateTime? LastExecutedAtUtc { get; set; }

    /// <summary>The owning Run's environment snapshot for the latest execution (Step 8 / TM-07 reuses this — never a second "latest environment" lookup).</summary>
    public string? LastEnvironmentNameSnapshot { get; set; }

    /// <summary>Set when the latest execution's own status was Failed/ExpectedFail (Step 8 / TM-07's "currently failing" list reuses this).</summary>
    public string? LastFailureMessage { get; set; }

    public bool IsFlaky { get; set; }
    public int? CurrentFailureSinceRunId { get; set; }
    public DateTime? CurrentFailureSinceUtc { get; set; }

    public int? LastPassRunId { get; set; }
    public DateTime? LastPassAtUtc { get; set; }
}

/// <summary>TM-04 (6.2) — one chronological execution of a TestCase, plus its derived transition (6.3).</summary>
public class TestHistoryEntryDto
{
    public int RunId { get; set; }
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;
    public DateTime? RunStartedAtUtc { get; set; }
    public int ScenarioResultId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public int? DurationMs { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>One of FirstResult / Regression / Recovery / StillFailing / StillPassing / Neutral (6.3).</summary>
    public string Transition { get; set; } = string.Empty;
}

public class TestCaseDetailDto : TestCaseSummaryDto
{
    public List<TestHistoryEntryDto> History { get; set; } = new();
}
