using System.Net;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Models;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// TM-06 (Step 7) — cleanup of explicitly-owned, platform-tracked test data
/// through the real OffenderWatch application API. Never scans the target
/// app; never accepts a raw target id/BaseUrl from the caller — every
/// destructive call is resolved entirely server-side from an existing
/// <see cref="TestDataRecord"/> row and its owning <see cref="TestRun"/>.
/// </summary>
public class TestDataService : ITestDataService
{
    private readonly TestManagementDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TestDataService> _logger;

    public TestDataService(TestManagementDbContext db, IHttpClientFactory httpClientFactory, ILogger<TestDataService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TestDataRecordDto>> GetAllAsync(string? status, string? entityType, int? runId, CancellationToken ct = default)
    {
        var query = _db.TestDataRecords.Include(r => r.TestRun).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestDataCleanupStatus>(status, ignoreCase: true, out var statusValue))
        {
            query = query.Where(r => r.CleanupStatus == statusValue);
        }
        if (!string.IsNullOrWhiteSpace(entityType) && Enum.TryParse<TestDataEntityType>(entityType, ignoreCase: true, out var entityTypeValue))
        {
            query = query.Where(r => r.EntityType == entityTypeValue);
        }
        if (runId.HasValue)
        {
            query = query.Where(r => r.TestRunId == runId.Value);
        }

        var records = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        return records.Select(ToDto).ToList();
    }

    public async Task<TestDataRecordDto> CleanAsync(int id, CancellationToken ct = default)
    {
        var record = await _db.TestDataRecords.Include(r => r.TestRun).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new TestDataRecordNotFoundException(id);

        await CleanOneAsync(record, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<IReadOnlyList<TestDataRecordDto>> CleanBatchAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        // 7.13 — the list must be explicit; an empty list is a validation
        // error, never interpreted as "clean everything".
        if (ids.Count == 0)
        {
            throw new TestDataValidationException("At least one TestDataRecord id is required.");
        }

        var records = await _db.TestDataRecords.Include(r => r.TestRun)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(ct);

        // 7.14 — LocationPoints before Offenders, deterministic, each record
        // processed independently: one failure never hides another's success.
        var ordered = records
            .OrderBy(r => r.EntityType == TestDataEntityType.LocationPoint ? 0 : 1)
            .ThenBy(r => r.Id)
            .ToList();

        foreach (var record in ordered)
        {
            await CleanOneAsync(record, ct);
        }
        await _db.SaveChangesAsync(ct);

        var byId = ordered.ToDictionary(r => r.Id);
        return ids.Where(byId.ContainsKey).Select(i => ToDto(byId[i])).ToList();
    }

    private async Task CleanOneAsync(TestDataRecord record, CancellationToken ct)
    {
        if (record.CleanupStatus == TestDataCleanupStatus.Cleaned)
        {
            return; // already cleaned — a harmless no-op, not an error (7.11: never re-delete an already-Cleaned record)
        }

        if (record.EntityType == TestDataEntityType.LocationPoint)
        {
            // 7.7 — verified directly against the real target API (its own
            // swagger contract, and a live probe during this step): there is
            // no endpoint to delete an individual location point, and
            // deleting the parent Offender does NOT cascade-delete its trail
            // data either. There is nothing safe to call, so nothing is
            // called — refusing beats fabricating a success.
            _logger.LogWarning("TestDataRecord {Id}: LocationPoint cleanup is not supported by the target API (no deletion endpoint exists)", record.Id);
            record.CleanupStatus = TestDataCleanupStatus.CleanupFailed;
            return;
        }

        if (record.EntityType != TestDataEntityType.Offender)
        {
            record.CleanupStatus = TestDataCleanupStatus.CleanupFailed;
            return;
        }

        // 7.9 — defense in depth: an explicit TestDataRecord row is
        // necessary but not sufficient on its own. The automation identifier
        // convention must ALSO hold before any destructive call is made —
        // if either check fails, refuse rather than risk seeded data.
        if (string.IsNullOrWhiteSpace(record.ExternalId)
            || string.IsNullOrWhiteSpace(record.Identifier)
            || !record.Identifier.StartsWith("AUTO", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "TestDataRecord {Id}: refused to clean — missing target id or Identifier does not start with the automation convention (seed-safety guard)",
                record.Id);
            record.CleanupStatus = TestDataCleanupStatus.CleanupFailed;
            return;
        }

        await DeleteOffenderAsync(record, ct);
    }

    private async Task DeleteOffenderAsync(TestDataRecord record, CancellationToken ct)
    {
        // 7.10/7.20 — the owning Run's immutable snapshot, never a live
        // re-read of the (possibly since-edited-or-deleted) Environment.
        var baseUrl = record.TestRun.BaseUrlSnapshot.TrimEnd('/');
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        try
        {
            var response = await client.DeleteAsync($"{baseUrl}/api/offenders/{record.ExternalId}", ct);

            // Empirically verified against the real target application
            // during this step: a genuine delete returns 204 (its own
            // swagger contract documents 200 — both are accepted as
            // success); a delete of an already-gone/unknown id reliably
            // returns 404 — exactly 7.12's "confirmed already missing"
            // signal, and only that signal, is ever treated as Cleaned.
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK)
            {
                record.CleanupStatus = TestDataCleanupStatus.Cleaned;
                record.CleanedAtUtc = DateTime.UtcNow;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                record.CleanupStatus = TestDataCleanupStatus.Cleaned;
                record.CleanedAtUtc = DateTime.UtcNow;
            }
            else
            {
                // 7.12 — a 5xx or any other ambiguous response is never
                // treated as "already gone".
                _logger.LogWarning("TestDataRecord {Id}: cleanup DELETE returned {Status}", record.Id, response.StatusCode);
                record.CleanupStatus = TestDataCleanupStatus.CleanupFailed;
            }
        }
        catch (Exception ex)
        {
            // 7.12 — a timeout or connection failure is never treated as
            // "already gone" either.
            _logger.LogWarning(ex, "TestDataRecord {Id}: cleanup request to the target application failed", record.Id);
            record.CleanupStatus = TestDataCleanupStatus.CleanupFailed;
        }
    }

    private static TestDataRecordDto ToDto(TestDataRecord r) => new()
    {
        Id = r.Id,
        TestRunId = r.TestRunId,
        ScenarioResultId = r.ScenarioResultId,
        EnvironmentNameSnapshot = r.TestRun.EnvironmentNameSnapshot,
        EntityType = r.EntityType.ToString(),
        ExternalId = r.ExternalId,
        Identifier = r.Identifier,
        CreatedAtUtc = r.CreatedAtUtc,
        CleanedAtUtc = r.CleanedAtUtc,
        CleanupStatus = r.CleanupStatus.ToString(),
    };
}
