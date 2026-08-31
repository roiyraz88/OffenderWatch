using System.Net;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Test-only <see cref="IHttpClientFactory"/> whose HttpClient never makes a
/// real network call — every request is answered deterministically by a
/// caller-supplied delegate. Used for TM-06 cleanup tests (7.22): they must
/// never make destructive calls against the real OffenderWatch environment.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    /// <summary>Convenience constructor for a fixed status code on every request.</summary>
    public static FakeHttpClientFactory RespondingWith(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));

    /// <summary>Convenience constructor that throws (simulating a timeout/connection failure).</summary>
    public static FakeHttpClientFactory ThrowingOn() =>
        new(_ => throw new HttpRequestException("simulated network failure"));

    public HttpClient CreateClient(string name) => new(new RecordingHandler(this));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly FakeHttpClientFactory _owner;

        public RecordingHandler(FakeHttpClientFactory owner)
        {
            _owner = owner;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _owner.Requests.Add(request);
            return Task.FromResult(_owner._respond(request));
        }
    }
}
