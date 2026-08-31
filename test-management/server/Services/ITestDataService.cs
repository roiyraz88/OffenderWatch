using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface ITestDataService
{
    Task<IReadOnlyList<TestDataRecordDto>> GetAllAsync(string? status, string? entityType, int? runId, CancellationToken ct = default);

    Task<TestDataRecordDto> CleanAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<TestDataRecordDto>> CleanBatchAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
}
