namespace OffenderWatch.TestManagement.Server.DTOs;

/// <summary>What the API returns for an Environment. Never the EF entity itself.</summary>
public class EnvironmentResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// POST /api/environments body. IsDefault is optional — if accepted, it
/// still goes through the same default-invariant enforcement as
/// PUT /api/environments/{id}/default (Step 3.4). CreatedAtUtc/UpdatedAtUtc
/// are never client-controlled.
/// </summary>
public class CreateEnvironmentRequest
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

/// <summary>PUT /api/environments/{id} body. Default status is changed only via the dedicated endpoint.</summary>
public class UpdateEnvironmentRequest
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
