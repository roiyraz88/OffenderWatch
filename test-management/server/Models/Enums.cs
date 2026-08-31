namespace OffenderWatch.TestManagement.Server.Models;

/// <summary>Lifecycle status of a <see cref="TestRun"/> as a whole.</summary>
public enum RunStatus
{
    Queued,
    Running,
    Completed,
    Stopped,
    Failed,
}

/// <summary>
/// What started a <see cref="TestRun"/>. "Scheduled" is intentionally not a
/// value yet — scheduled execution is the optional B-04 bonus, not part of
/// TM-01..TM-08.
/// </summary>
public enum RunTrigger
{
    Manual,
    Api,
}

/// <summary>Which automation project a <see cref="TestCase"/> belongs to.</summary>
public enum TestSuite
{
    Ui,
    Api,
}

/// <summary>
/// Lifecycle status of a single <see cref="ScenarioResult"/>. ExpectedFail is
/// distinct from Failed so a documented [BUG-xxx] failure never counts as an
/// unexpected regression (see Development Rule 7 / TM-02's totals).
/// </summary>
public enum ScenarioStatus
{
    Queued,
    Running,
    Passed,
    Failed,
    ExpectedFail,
    Skipped,
    Cancelled,
}

/// <summary>Kind of evidence stored for one <see cref="ScenarioResult"/>.</summary>
public enum EvidenceType
{
    Log,
    Screenshot,
    ApiRequest,
    ApiResponse,
    Trace,
}

/// <summary>Kind of application entity a <see cref="TestDataRecord"/> tracks.</summary>
public enum TestDataEntityType
{
    Offender,
    LocationPoint,
}

/// <summary>Cleanup lifecycle of a <see cref="TestDataRecord"/>.</summary>
public enum TestDataCleanupStatus
{
    Active,
    Cleaned,
    CleanupFailed,
}
