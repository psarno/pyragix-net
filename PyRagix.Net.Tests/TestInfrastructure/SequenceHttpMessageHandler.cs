using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PyRagix.Net.Tests.TestInfrastructure;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> that replays queued behaviours for unit tests.
/// </summary>
internal sealed class SequenceHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps;

    public SequenceHttpMessageHandler(IEnumerable<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> steps)
    {
        _steps = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(steps);
    }

    public int CallCount { get; private set; }

    public string? LastRequestBody { get; private set; }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception)
    {
        return (_, _) => Task.FromException<HttpResponseMessage>(exception);
    }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(string json)
    {
        return (_, _) =>
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(message);
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        if (request.Content != null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("No handlers remaining in sequence.");
        }

        var handler = _steps.Dequeue();
        return await handler(request, cancellationToken);
    }
}
