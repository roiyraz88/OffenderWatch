using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface IRunService
{
    Task<RunSummaryDto> CreateAsync(CreateRunRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RunSummaryDto>> GetAllAsync(CancellationToken ct = default);

    Task<RunDetailDto> GetByIdAsync(int id, CancellationToken ct = default);

    Task StopAsync(int id, CancellationToken ct = default);
}
