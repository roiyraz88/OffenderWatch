using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface IRunService
{
    Task<RunSummaryDto> CreateAsync(CreateRunRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RunSummaryDto>> GetAllAsync(CancellationToken ct = default);

    Task<RunDetailDto> GetByIdAsync(int id, CancellationToken ct = default);

    Task StopAsync(int id, CancellationToken ct = default);

    /// <summary>TM-08 (6.18) — evidence metadata for one ScenarioResult, validated as belonging to the given Run.</summary>
    Task<IReadOnlyList<EvidenceArtifactDto>> GetScenarioEvidenceAsync(int runId, int scenarioResultId, CancellationToken ct = default);
}
