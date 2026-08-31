using Microsoft.AspNetCore.SignalR;

namespace OffenderWatch.TestManagement.Server.Hubs;

/// <summary>
/// TM-03 (Step 5) — the real-time transport for run/scenario updates.
/// Deliberately thin (5.1): no execution logic lives here at all, only
/// connection/group membership. <see cref="Services.RunOrchestrator"/> and
/// <see cref="Services.RunService"/> are the only things that ever broadcast
/// into a run's group; the Hub itself never mutates a Run or ScenarioResult
/// (5.15) — clients may only subscribe/unsubscribe.
/// </summary>
public class RunHub : Hub
{
    /// <summary>The one run-group naming convention (5.2), shared by every broadcaster.</summary>
    public static string GroupName(int runId) => $"run:{runId}";

    public Task SubscribeToRun(int runId)
    {
        if (runId <= 0)
        {
            return Task.CompletedTask;
        }
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));
    }

    public Task UnsubscribeFromRun(int runId)
    {
        if (runId <= 0)
        {
            return Task.CompletedTask;
        }
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));
    }
}
