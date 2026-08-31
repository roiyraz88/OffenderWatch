using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}
