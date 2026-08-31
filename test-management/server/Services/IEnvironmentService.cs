using OffenderWatch.TestManagement.Server.DTOs;

namespace OffenderWatch.TestManagement.Server.Services;

public interface IEnvironmentService
{
    Task<IReadOnlyList<EnvironmentResponseDto>> GetAllAsync(CancellationToken ct = default);

    Task<EnvironmentResponseDto> GetByIdAsync(int id, CancellationToken ct = default);

    Task<EnvironmentResponseDto> CreateAsync(CreateEnvironmentRequest request, CancellationToken ct = default);

    Task<EnvironmentResponseDto> UpdateAsync(int id, UpdateEnvironmentRequest request, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    Task<EnvironmentResponseDto> SetDefaultAsync(int id, CancellationToken ct = default);
}
