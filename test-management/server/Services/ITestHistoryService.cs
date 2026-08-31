using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface ITestHistoryService
{
    Task<IReadOnlyList<TestCaseSummaryDto>> GetAllAsync(CancellationToken ct = default);

    Task<TestCaseDetailDto> GetHistoryAsync(int testCaseId, CancellationToken ct = default);
}
