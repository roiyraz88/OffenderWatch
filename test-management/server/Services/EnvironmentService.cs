using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using Environment = OffenderWatch.TestManagement.Server.Models.Environment;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// TM-01. Owns every default-Environment invariant (Step 3.4) and all
/// backend validation (Step 3.3) — the frontend's own checks are a
/// convenience only, this is the source of truth.
/// </summary>
public class EnvironmentService : IEnvironmentService
{
    private readonly TestManagementDbContext _db;

    public EnvironmentService(TestManagementDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<EnvironmentResponseDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Environments
            .OrderBy(e => e.Name)
            .Select(e => ToDto(e))
            .ToListAsync(ct);
    }

    public async Task<EnvironmentResponseDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var environment = await FindOrThrowAsync(id, ct);
        return ToDto(environment);
    }

    public async Task<EnvironmentResponseDto> CreateAsync(CreateEnvironmentRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);
        var baseUrl = ValidateBaseUrl(request.BaseUrl);
        await EnsureNameIsUniqueAsync(name, excludeId: null, ct);

        var now = DateTime.UtcNow;
        var isFirstEnvironment = !await _db.Environments.AnyAsync(ct);

        var environment = new Environment
        {
            Name = name,
            BaseUrl = baseUrl,
            // Rule 1: the very first Environment is always default,
            // regardless of what the client requested.
            IsDefault = isFirstEnvironment,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Rule 2: an explicitly-requested default (that isn't already
        // covered by "first environment") atomically unseats the old one.
        if (!isFirstEnvironment && request.IsDefault)
        {
            await UnsetCurrentDefaultAsync(ct);
            environment.IsDefault = true;
        }

        _db.Environments.Add(environment);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToDto(environment);
    }

    public async Task<EnvironmentResponseDto> UpdateAsync(int id, UpdateEnvironmentRequest request, CancellationToken ct = default)
    {
        var environment = await FindOrThrowAsync(id, ct);

        var name = ValidateName(request.Name);
        var baseUrl = ValidateBaseUrl(request.BaseUrl);
        await EnsureNameIsUniqueAsync(name, excludeId: id, ct);

        environment.Name = name;
        environment.BaseUrl = baseUrl;
        environment.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToDto(environment);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var environment = await FindOrThrowAsync(id, ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var wasDefault = environment.IsDefault;

        // Historical TestRuns are preserved by design: the FK is nullable
        // with ON DELETE SET NULL (Step 2), and each TestRun already froze
        // its own EnvironmentNameSnapshot/BaseUrlSnapshot at creation time.
        // Nothing here needs to touch TestRun rows at all.
        _db.Environments.Remove(environment);
        await _db.SaveChangesAsync(ct);

        if (wasDefault)
        {
            // Rule 6: promote another remaining Environment to default.
            // Rule 7: if none remain, zero defaults is valid — nothing to do.
            var replacement = await _db.Environments
                .OrderBy(e => e.Id)
                .FirstOrDefaultAsync(ct);

            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<EnvironmentResponseDto> SetDefaultAsync(int id, CancellationToken ct = default)
    {
        var environment = await FindOrThrowAsync(id, ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        await UnsetCurrentDefaultAsync(ct);

        environment.IsDefault = true;
        environment.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return ToDto(environment);
    }

    // ---- helpers -----------------------------------------------------

    private async Task<Environment> FindOrThrowAsync(int id, CancellationToken ct)
    {
        var environment = await _db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (environment is null)
        {
            throw new EnvironmentNotFoundException(id);
        }
        return environment;
    }

    /// <summary>Unsets whichever Environment is currently default, if any. Caller owns the transaction.</summary>
    private async Task UnsetCurrentDefaultAsync(CancellationToken ct)
    {
        var currentDefault = await _db.Environments.FirstOrDefaultAsync(e => e.IsDefault, ct);
        if (currentDefault is not null)
        {
            currentDefault.IsDefault = false;
            currentDefault.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task EnsureNameIsUniqueAsync(string name, int? excludeId, CancellationToken ct)
    {
        var lowerName = name.ToLowerInvariant();
        var query = _db.Environments.Where(e => e.Name.ToLower() == lowerName);
        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        if (await query.AnyAsync(ct))
        {
            throw new EnvironmentConflictException($"An environment named '{name}' already exists.");
        }
    }

    private static string ValidateName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new EnvironmentValidationException("Name is required.");
        }
        if (trimmed.Length > 200)
        {
            throw new EnvironmentValidationException("Name must be 200 characters or fewer.");
        }
        return trimmed;
    }

    private static string ValidateBaseUrl(string? baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new EnvironmentValidationException("Base URL is required.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new EnvironmentValidationException(
                "Base URL must be an absolute http:// or https:// URL.");
        }

        if (trimmed.Length > 500)
        {
            throw new EnvironmentValidationException("Base URL must be 500 characters or fewer.");
        }

        return trimmed;
    }

    private static EnvironmentResponseDto ToDto(Environment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        BaseUrl = e.BaseUrl,
        IsDefault = e.IsDefault,
        CreatedAtUtc = e.CreatedAtUtc,
        UpdatedAtUtc = e.UpdatedAtUtc,
    };
}
