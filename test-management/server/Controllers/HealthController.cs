using Microsoft.AspNetCore.Mvc;

namespace OffenderWatch.TestManagement.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "OffenderWatch Test Management API",
            timestampUtc = DateTime.UtcNow
        });
    }
}
