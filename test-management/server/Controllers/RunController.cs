using Microsoft.AspNetCore.Mvc;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>TM-02 — Run execution &amp; management. Thin HTTP layer; all orchestration lives in <see cref="IRunService"/> / <see cref="RunOrchestrator"/>.</summary>
[ApiController]
[Route("api/runs")]
public class RunController : ControllerBase
{
    private readonly IRunService _runs;

    public RunController(IRunService runs)
    {
        _runs = runs;
    }

    /// <summary>Creates and enqueues a run; returns promptly (4.1) — execution happens in the background.</summary>
    [HttpPost]
    public async Task<ActionResult<RunSummaryDto>> Create([FromBody] CreateRunRequest request, CancellationToken ct)
    {
        var created = await _runs.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status202Accepted, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RunSummaryDto>>> GetAll(CancellationToken ct)
    {
        return Ok(await _runs.GetAllAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RunDetailDto>> GetById(int id, CancellationToken ct)
    {
        return Ok(await _runs.GetByIdAsync(id, ct));
    }

    [HttpPost("{id:int}/stop")]
    public async Task<IActionResult> Stop(int id, CancellationToken ct)
    {
        await _runs.StopAsync(id, ct);
        return NoContent();
    }

    /// <summary>TM-08 (6.18) — evidence metadata for one ScenarioResult of this Run.</summary>
    [HttpGet("{runId:int}/scenarios/{scenarioResultId:int}/evidence")]
    public async Task<ActionResult<IReadOnlyList<EvidenceArtifactDto>>> GetScenarioEvidence(int runId, int scenarioResultId, CancellationToken ct)
    {
        return Ok(await _runs.GetScenarioEvidenceAsync(runId, scenarioResultId, ct));
    }
}
