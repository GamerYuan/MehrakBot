using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Helpers;

internal static class LoggerMock
{
    // For internal service types not visible to this assembly: Mock<ILogger<T>> cannot be named directly
    public static ILogger For(Type serviceType) =>
        (ILogger)((Mock)Activator.CreateInstance(
            typeof(Mock<>).MakeGenericType(typeof(ILogger<>).MakeGenericType(serviceType)))!).Object;
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> m_Responders = new();

    public int RequestCount { get; private set; }

    public void Enqueue(Func<HttpResponseMessage> responder)
    {
        m_Responders.Enqueue(_ => responder());
    }

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(m_Responders.Dequeue()(request));
    }

    public IHttpClientFactory ToHttpClientFactory()
    {
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(this));
        return mock.Object;
    }
}
