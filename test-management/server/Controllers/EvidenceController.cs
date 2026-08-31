using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.Services;

namespace OffenderWatch.TestManagement.Server.Controllers;

/// <summary>
/// TM-08 (6.18) — safe evidence content retrieval. Never trusts the
/// persisted RelativePath blindly: every request re-resolves it against the
/// configured artifact root and re-verifies containment (no path
/// traversal) and existence, even though the path was already validated
/// once at ingestion time (<see cref="RunOrchestrator"/>) — defense in
/// depth for a value that ultimately reaches the filesystem.
/// </summary>
[ApiController]
[Route("api/evidence")]
public class EvidenceController : ControllerBase
{
    private readonly TestManagementDbContext _db;
    private readonly RunnerOptions _options;
    private readonly IHostEnvironment _hostEnvironment;

    public EvidenceController(TestManagementDbContext db, IOptions<RunnerOptions> options, IHostEnvironment hostEnvironment)
    {
        _db = db;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> GetContent(int id, CancellationToken ct)
    {
        var artifact = await _db.EvidenceArtifacts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (artifact is null)
        {
            return NotFound();
        }

        var artifactRoot = Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, _options.ArtifactRootRelativeToContentRoot));
        var candidate = Path.GetFullPath(Path.Combine(artifactRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var isInsideArtifactRoot = candidate.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!isInsideArtifactRoot || !System.IO.File.Exists(candidate))
        {
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(candidate);
        var contentType = string.IsNullOrWhiteSpace(artifact.ContentType) ? "application/octet-stream" : artifact.ContentType;
        return File(stream, contentType);
    }
}
