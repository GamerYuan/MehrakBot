using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Mehrak.Domain.Shared.Telemetry;
using Mehrak.ServiceDefaults;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Mehrak.Application.Tests.Shared.Services;

/// <summary>
/// Exercises the real <c>System.Net.Http</c> instrumentation so the expected-miss processor is verified against the
/// tags and status assigned by the instrumentation, not against a hand-built activity.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ExpectedDependencyTracingTests
{
    [Test]
    public async Task Http404_WithExistenceProbeIntent_IsClassifiedAsExpectedMiss()
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(HttpStatusCode.NotFound);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using (ExpectedDependency.Begin(ExpectedDependencyKind.ExistenceProbe))
        using (var response = await client.GetAsync(server.RequestUri))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        await server.Completion;
        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.SourceName, Is.EqualTo("System.Net.Http"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags.ContainsKey("error.type"), Is.False);
            Assert.That(span.Tags[ExpectedDependency.KindTag], Is.EqualTo(ExpectedDependency.ExistenceProbeKind));
            Assert.That(span.Tags[ExpectedDependency.OutcomeTag], Is.EqualTo(ExpectedDependency.ExpectedMissOutcome));
            Assert.That(span.Tags["http.response.status_code"], Is.EqualTo(404));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        });
    }

    [Test]
    public async Task Http404_WithWikiIntent_IsClassifiedAsExpectedMiss()
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(HttpStatusCode.NotFound);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using (ExpectedDependency.Begin(ExpectedDependencyKind.WikiContentLookup))
        using (var response = await client.GetAsync(server.RequestUri))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        await server.Completion;
        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags.ContainsKey("error.type"), Is.False);
            Assert.That(span.Tags[ExpectedDependency.KindTag], Is.EqualTo(ExpectedDependency.WikiContentKind));
            Assert.That(span.Tags[ExpectedDependency.OutcomeTag], Is.EqualTo(ExpectedDependency.ExpectedMissOutcome));
        });
    }

    [Test]
    public async Task Http404_WithoutIntent_RemainsAnError()
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(HttpStatusCode.NotFound);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using var response = await client.GetAsync(server.RequestUri);
        await server.Completion;

        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags.ContainsKey("error.type"), Is.True);
            Assert.That(span.Tags.ContainsKey(ExpectedDependency.KindTag), Is.False);
            Assert.That(span.Tags.ContainsKey(ExpectedDependency.OutcomeTag), Is.False);
        });
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    public async Task Non404HttpFailure_WithWikiIntent_RemainsAnError(HttpStatusCode statusCode)
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(statusCode);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using (ExpectedDependency.Begin(ExpectedDependencyKind.WikiContentLookup))
        using (var response = await client.GetAsync(server.RequestUri))
        {
            Assert.That(response.StatusCode, Is.EqualTo(statusCode));
        }

        await server.Completion;

        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags.ContainsKey("error.type"), Is.True);
            Assert.That(span.Tags[ExpectedDependency.KindTag], Is.EqualTo(ExpectedDependency.WikiContentKind));
            Assert.That(span.Tags.ContainsKey(ExpectedDependency.OutcomeTag), Is.False);
        });
    }

    [Test]
    public async Task HttpServerError_WithExistenceProbeIntent_RemainsAnError()
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using (ExpectedDependency.Begin(ExpectedDependencyKind.ExistenceProbe))
        using (var response = await client.GetAsync(server.RequestUri))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        }

        await server.Completion;

        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Tags.ContainsKey("error.type"), Is.True);
            Assert.That(span.Tags.ContainsKey(ExpectedDependency.OutcomeTag), Is.False);
        });
    }

    [Test]
    public async Task HttpSuccess_WithProbeIntent_KeepsKindWithoutExpectedMissTag()
    {
        using var capture = new ActivityCapture();
        using var provider = CreateProvider(capture);
        await using var server = LoopbackHttpServer.Start(HttpStatusCode.OK);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        using (ExpectedDependency.Begin(ExpectedDependencyKind.ExistenceProbe))
        using (var response = await client.GetAsync(server.RequestUri))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await server.Completion;

        var span = capture.SingleHttpSpan();
        Assert.Multiple(() =>
        {
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
            Assert.That(span.Tags[ExpectedDependency.KindTag], Is.EqualTo(ExpectedDependency.ExistenceProbeKind));
            Assert.That(span.Tags.ContainsKey(ExpectedDependency.OutcomeTag), Is.False);
        });
    }

    private static TracerProvider CreateProvider(ActivityCapture capture)
    {
        return Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddHttpClientInstrumentation()
            .AddExpectedDependencyNormalization()
            .AddProcessor(capture)
            .Build();
    }

    private sealed class ActivityCapture : BaseProcessor<Activity>
    {
        private readonly List<CapturedSpan> m_Spans = [];

        public override void OnEnd(Activity data)
        {
            var tags = data.TagObjects.ToDictionary(pair => pair.Key, pair => pair.Value);
            lock (m_Spans)
                m_Spans.Add(new CapturedSpan(data.Source.Name, data.Kind, data.Status, tags));
        }

        public CapturedSpan SingleHttpSpan()
        {
            lock (m_Spans)
                return m_Spans.Single(span => span.Tags.ContainsKey("http.response.status_code"));
        }
    }

    private sealed record CapturedSpan(string SourceName, ActivityKind Kind, ActivityStatusCode Status,
        IReadOnlyDictionary<string, object?> Tags);

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener m_Listener;
        private readonly Task m_ResponseTask;

        private LoopbackHttpServer(TcpListener listener, HttpStatusCode statusCode)
        {
            m_Listener = listener;
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            RequestUri = new Uri($"http://127.0.0.1:{port}/probe");
            m_ResponseTask = RespondAsync(statusCode);
        }

        public Uri RequestUri { get; }

        public Task Completion => m_ResponseTask;

        public static LoopbackHttpServer Start(HttpStatusCode statusCode)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new LoopbackHttpServer(listener, statusCode);
        }

        private async Task RespondAsync(HttpStatusCode statusCode)
        {
            using var client = await m_Listener.AcceptTcpClientAsync().ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024,
                leaveOpen: true);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 })
            {
            }

            var response =
                $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            m_Listener.Stop();
            try
            {
                await m_ResponseTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
