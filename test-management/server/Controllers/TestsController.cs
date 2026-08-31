using Microsoft.AspNetCore.Mvc;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>TM-04 — derived test history. Thin; all derivation lives in <see cref="ITestHistoryService"/>/<see cref="HistoryClassifier"/>.</summary>
[ApiController]
[Route("api/tests")]
public class TestsController : ControllerBase
{
    private readonly ITestHistoryService _tests;

    public TestsController(ITestHistoryService tests)
    {
        _tests = tests;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TestCaseSummaryDto>>> GetAll(CancellationToken ct)
    {
        return Ok(await _tests.GetAllAsync(ct));
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<TestCaseDetailDto>> GetHistory(int id, CancellationToken ct)
    {
        return Ok(await _tests.GetHistoryAsync(id, ct));
    }
}
