namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>TM-07 (8.2) — the one purpose-built payload GET /api/dashboard returns. No EF entity, no raw Run/ScenarioResult dump.</summary>
public class DashboardDto
{
    public DateTime GeneratedAtUtc { get; set; }

    /// <summary>One of Go / NoGo / Incomplete / NoData (8.9).</summary>
    public string OverallDecision { get; set; } = string.Empty;

    public int? LatestRelevantRunId { get; set; }
    public double? LatestRunPassRate { get; set; }
    public int LatestRunUnexpectedFailedCount { get; set; }
    public int LatestRunExpectedFailedCount { get; set; }
    public int CurrentlyFailingTestCount { get; set; }

    public List<DashboardEnvironmentRunDto> LatestRunsByEnvironment { get; set; } = new();
    public List<DashboardTrendPointDto> PassRateTrend { get; set; } = new();
    public List<DashboardCurrentlyFailingTestDto> CurrentlyFailingTests { get; set; } = new();
}

/// <summary>8.3 — one Environment's most recent relevant Run, from its immutable historical snapshot.</summary>
public class DashboardEnvironmentRunDto
{
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;
    public string BaseUrlSnapshot { get; set; } = string.Empty;
    public int RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double? DurationSeconds { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int ExpectedFailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalScenarioCount { get; set; }
    public double? PassRate { get; set; }
}

/// <summary>8.5 — one chronological pass-rate trend point.</summary>
public class DashboardTrendPointDto
{
    public int RunId { get; set; }
    public string EnvironmentNameSnapshot { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public double? PassRate { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int ExpectedFailedCount { get; set; }
    public int TotalComparableCount { get; set; }
}

/// <summary>8.6 — one TestCase whose latest comparable result is Failed or ExpectedFail.</summary>
public class DashboardCurrentlyFailingTestDto
{
    public int TestCaseId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Suite { get; set; } = string.Empty;
    public string? RequirementId { get; set; }
    public string? BugId { get; set; }

    /// <summary>Failed or ExpectedFail (8.6/8.8 — kept visually distinct on the client).</summary>
    public string CurrentStatus { get; set; } = string.Empty;

    public int LatestRunId { get; set; }
    public string LatestEnvironmentNameSnapshot { get; set; } = string.Empty;

    public DateTime? CurrentFailureSinceUtc { get; set; }
    public int? CurrentFailureSinceRunId { get; set; }

    /// <summary>GeneratedAtUtc - CurrentFailureSinceUtc, computed on read — never persisted (8.7).</summary>
    public double? FailureDurationSeconds { get; set; }

    public string? LatestFailureMessage { get; set; }
}
