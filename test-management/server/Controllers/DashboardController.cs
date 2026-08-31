using Microsoft.AspNetCore.Mvc;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>TM-07 — the one release-overview endpoint. Thin; all aggregation lives in <see cref="IDashboardService"/>.</summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
    {
        return Ok(await _dashboard.GetAsync(ct));
    }
}
