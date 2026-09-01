using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>Bonus B-02 — Run Comparison.</summary>
public interface IRunComparisonService
{
    Task<RunComparisonDto> CompareAsync(int baseRunId, int compareRunId, CancellationToken ct = default);
}
