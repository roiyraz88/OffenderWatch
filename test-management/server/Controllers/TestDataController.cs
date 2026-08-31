using Microsoft.AspNetCore.Mvc;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>
/// TM-06 — test-data lifecycle. The browser only ever sends TestDataRecord
/// ids; the backend resolves the actual target entity/URL server-side
/// (7.8) — there is no way to pass an arbitrary BaseUrl or target id
/// through this API.
/// </summary>
[ApiController]
[Route("api/test-data")]
public class TestDataController : ControllerBase
{
    private readonly ITestDataService _testData;

    public TestDataController(ITestDataService testData)
    {
        _testData = testData;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TestDataRecordDto>>> GetAll(
        [FromQuery] string? status, [FromQuery] string? entityType, [FromQuery] int? runId, CancellationToken ct)
    {
        return Ok(await _testData.GetAllAsync(status, entityType, runId, ct));
    }

    [HttpPost("{id:int}/clean")]
    public async Task<ActionResult<TestDataRecordDto>> Clean(int id, CancellationToken ct)
    {
        return Ok(await _testData.CleanAsync(id, ct));
    }

    [HttpPost("clean")]
    public async Task<ActionResult<IReadOnlyList<TestDataRecordDto>>> CleanBatch([FromBody] CleanTestDataBatchRequest request, CancellationToken ct)
    {
        return Ok(await _testData.CleanBatchAsync(request.Ids, ct));
    }
}
