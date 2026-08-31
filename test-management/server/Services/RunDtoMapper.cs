using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// Entity -&gt; DTO mapping shared by <see cref="RunService"/> (REST responses)
/// and <see cref="RunOrchestrator"/> (Step 5 SignalR broadcasts) so both
/// transports describe a Run/ScenarioResult identically. No EF entity is
/// ever sent over either transport (5.3).
/// </summary>
public static class RunDtoMapper
{
    public static RunSummaryDto ToSummaryDto(TestRun r) => new()
    {
        Id = r.Id,
        EnvironmentId = r.EnvironmentId,
        EnvironmentNameSnapshot = r.EnvironmentNameSnapshot,
        BaseUrlSnapshot = r.BaseUrlSnapshot,
        Status = r.Status.ToString(),
        Trigger = r.Trigger.ToString(),
        CreatedAtUtc = r.CreatedAtUtc,
        StartedAtUtc = r.StartedAtUtc,
        EndedAtUtc = r.EndedAtUtc,
        DurationSeconds = r.StartedAtUtc.HasValue && r.EndedAtUtc.HasValue
            ? (r.EndedAtUtc.Value - r.StartedAtUtc.Value).TotalSeconds
            : null,
        PassedCount = r.PassedCount,
        FailedCount = r.FailedCount,
        ExpectedFailedCount = r.ExpectedFailedCount,
        SkippedCount = r.SkippedCount,
    };

    /// <summary>Requires <paramref name="sr"/>.TestCase to be loaded/attached.</summary>
    public static ScenarioResultDto ToScenarioResultDto(ScenarioResult sr) => new()
    {
        Id = sr.Id,
        TestCaseId = sr.TestCaseId,
        ExternalId = sr.TestCase.ExternalId,
        Name = sr.TestCase.Name,
        Suite = sr.TestCase.Suite.ToString(),
        RequirementId = sr.TestCase.RequirementId,
        BugId = sr.TestCase.BugId,
        Status = sr.Status.ToString(),
        StartedAtUtc = sr.StartedAtUtc,
        EndedAtUtc = sr.EndedAtUtc,
        DurationMs = sr.DurationMs,
        FailureMessage = sr.FailureMessage,
        StackTrace = sr.StackTrace,
    };
}
