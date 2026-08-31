using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using OffenderWatch.TestManagement.Server.Hubs;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Test-only helpers for getting an <see cref="IHubContext{RunHub}"/> without
/// a running Kestrel host. <see cref="Real"/> is ASP.NET Core's own
/// SignalR DI wiring (a genuine <c>DefaultHubLifetimeManager</c> with zero
/// connected clients — not a hand-rolled mock) — broadcasting through it is
/// a real no-op, exactly like production with no browser connected.
/// <see cref="ThrowingHubContext"/> exists only to prove 5.5's requirement
/// that a transport failure can never affect a run's persisted outcome.
/// </summary>
internal static class TestHubContext
{
    public static IHubContext<RunHub> Real()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        return services.BuildServiceProvider().GetRequiredService<IHubContext<RunHub>>();
    }
}

/// <summary>An <see cref="IHubContext{RunHub}"/> whose every send throws, simulating a broken SignalR transport.</summary>
internal sealed class ThrowingHubContext : IHubContext<RunHub>
{
    public IHubClients Clients { get; } = new ThrowingHubClients();
    public IGroupManager Groups => throw new NotSupportedException("Not needed for these tests.");

    private sealed class ThrowingHubClients : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => new ThrowingClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class ThrowingClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated SignalR transport failure.");
    }
}
