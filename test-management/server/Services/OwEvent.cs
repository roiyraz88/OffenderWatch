using System.Text.Json;
using System.Text.Json.Serialization;

namespace OffenderWatch.TestManagement.Server.Services;

/// <summary>
/// The OW_EVENT|{json} runner protocol (Step 4.7). One flexible envelope
/// covers every event type's field union — simpler than a polymorphic
/// hierarchy for four small, closely-related event shapes.
/// </summary>
public class OwEvent
{
    public int Version { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Runner { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }

    // scenario_* events
    public string? ExternalId { get; set; }
    public string? Name { get; set; }
    public string? Suite { get; set; }
    public string? RequirementId { get; set; }
    public string? BugId { get; set; }

    // scenario_finished
    public string? Status { get; set; }
    public long? DurationMs { get; set; }
    public string? FailureMessage { get; set; }
    public string? StackTrace { get; set; }
    public bool? NativeExpectedFailure { get; set; }

    // suite_finished
    public int? TotalScenarios { get; set; }
}

public static class OwEventParser
{
    private const string Prefix = "OW_EVENT|";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns null for any line that isn't a well-formed OW_EVENT line —
    /// ordinary runner console output is expected and silently ignored
    /// (4.7: "The backend must ignore ordinary stdout/stderr lines that do
    /// not begin with OW_EVENT|"). A malformed OW_EVENT line is logged by
    /// the caller, not thrown — one bad line must never crash the run.
    /// </summary>
    public static bool TryParse(string? line, out OwEvent? owEvent)
    {
        owEvent = null;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var prefixIndex = line.IndexOf(Prefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return false;
        }

        var json = line[(prefixIndex + Prefix.Length)..];
        try
        {
            owEvent = JsonSerializer.Deserialize<OwEvent>(json, JsonOptions);
            return owEvent is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
