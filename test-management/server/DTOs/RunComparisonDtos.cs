namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>Bonus B-02 — one metric's Base -&gt; Compare movement.</summary>
public class MetricDeltaDto
{
    public int Base { get; set; }
    public int Compare { get; set; }
    public int Delta { get; set; }
}

/// <summary>Bonus B-02 — the totals delta block (assignment example: Passed/Failed/ExpectedFail/Skipped/Total).</summary>
public class TotalsDeltaDto
{
    public MetricDeltaDto Passed { get; set; } = new();
    public MetricDeltaDto Failed { get; set; } = new();
    public MetricDeltaDto ExpectedFail { get; set; } = new();
    public MetricDeltaDto Skipped { get; set; } = new();
    public MetricDeltaDto Total { get; set; } = new();
}

/// <summary>Bonus B-02 — counts per <see cref="Services.ComparisonChangeType"/>, for the QA-facing summary cards.</summary>
public class ComparisonSummaryDto
{
    public int Regressions { get; set; }
    public int Recoveries { get; set; }
    public int New { get; set; }
    public int Missing { get; set; }
    public int StillPassing { get; set; }
    public int StillFailing { get; set; }
    public int ExpectedFailures { get; set; }
    public int OtherChanges { get; set; }
    public int Unchanged { get; set; }
}

/// <summary>Bonus B-02 — one TestCase's Base -&gt; Compare row. BaseStatus/CompareStatus are null exactly when the TestCase did not run in that run (New/Missing).</summary>
public class TestComparisonEntryDto
{
    public int TestCaseId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Suite { get; set; } = string.Empty;
    public string? RequirementId { get; set; }
    public string? BugId { get; set; }
    public string? BaseStatus { get; set; }
    public string? CompareStatus { get; set; }

    /// <summary>One of <see cref="Services.ComparisonChangeType"/>, as a string.</summary>
    public string Change { get; set; } = string.Empty;
}

/// <summary>Bonus B-02 — GET /api/runs/compare?baseRunId=&amp;compareRunId= response. Entirely derived on read from existing persisted TestRun/ScenarioResult/TestCase data — nothing new is stored.</summary>
public class RunComparisonDto
{
    public RunSummaryDto BaseRun { get; set; } = null!;
    public RunSummaryDto CompareRun { get; set; } = null!;

    /// <summary>True when the two runs' immutable EnvironmentNameSnapshot/BaseUrlSnapshot differ — the UI must surface this prominently, never hide it.</summary>
    public bool EnvironmentsDiffer { get; set; }

    /// <summary>True when a run's Status is not Completed (Queued/Running/Stopped/Failed) — the comparison still runs, but the UI must warn it may not reflect a complete suite.</summary>
    public bool BaseRunIncomplete { get; set; }
    public bool CompareRunIncomplete { get; set; }

    public TotalsDeltaDto TotalsDelta { get; set; } = new();
    public ComparisonSummaryDto Summary { get; set; } = new();
    public List<TestComparisonEntryDto> Tests { get; set; } = new();
}
