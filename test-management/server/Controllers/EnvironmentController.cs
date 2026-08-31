using Microsoft.AspNetCore.Mvc;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>TM-01 — Environment configuration. Thin HTTP layer; all rules live in <see cref="IEnvironmentService"/>.</summary>
[ApiController]
[Route("api/environments")]
public class EnvironmentController : ControllerBase
{
    private readonly IEnvironmentService _environments;

    public EnvironmentController(IEnvironmentService environments)
    {
        _environments = environments;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnvironmentResponseDto>>> GetAll(CancellationToken ct)
    {
        return Ok(await _environments.GetAllAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EnvironmentResponseDto>> GetById(int id, CancellationToken ct)
    {
        return Ok(await _environments.GetByIdAsync(id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<EnvironmentResponseDto>> Create(
        [FromBody] CreateEnvironmentRequest request, CancellationToken ct)
    {
        var created = await _environments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EnvironmentResponseDto>> Update(
        int id, [FromBody] UpdateEnvironmentRequest request, CancellationToken ct)
    {
        return Ok(await _environments.UpdateAsync(id, request, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _environments.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/default")]
    public async Task<ActionResult<EnvironmentResponseDto>> SetDefault(int id, CancellationToken ct)
    {
        return Ok(await _environments.SetDefaultAsync(id, ct));
    }
}
