#region

using System.Net;
using System.Text.Json.Nodes;
using MehrakCore.Services;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;

#endregion

namespace MehrakCore.Tests.Services.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInServiceTests
{
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<ILogger<DailyCheckInService>> m_LoggerMock;
    private DiscordTestHelper m_DiscordTestHelper;
    private Mock<HttpMessageHandler> m_MockHttpMessageHandler;

    [SetUp]
    public void Setup()
    {
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_LoggerMock = new Mock<ILogger<DailyCheckInService>>();
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MockHttpMessageHandler = new Mock<HttpMessageHandler>();

        // Setup HttpClient with mock handler
        var httpClient = new HttpClient(m_MockHttpMessageHandler.Object);
        m_HttpClientFactoryMock.Setup(factory => factory.CreateClient("Default")).Returns(httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
    }

    [Test]
    public async Task CheckInAsync_SuccessfulCheckIn_SendsSuccessMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";

        // Setup HTTP response for successful check-in (retcode 0)
        var jsonResponse = new JsonObject
        {
            ["retcode"] = 0,
            ["message"] = "OK",
            ["data"] = new JsonObject()
        };

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse.ToJsonString());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);

        var service = new DailyCheckInService(m_HttpClientFactoryMock.Object, m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("Check in successful").IgnoreCase);

        // Verify HTTP request had proper headers
        m_MockHttpMessageHandler.Protected()
            .Verify("SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(req =>
                    req.Headers.Contains("Cookie") &&
                    req.Headers.GetValues("Cookie").Any(v =>
                        v.Contains($"ltuid_v2={ltuid}") && v.Contains($"ltoken_v2={ltoken}"))),
                ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task CheckInAsync_AlreadyCheckedIn_SendsAlreadyCheckedInMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";

        // Setup HTTP response for already checked in (retcode -5003)
        var jsonResponse = new JsonObject
        {
            ["retcode"] = -5003,
            ["message"] = "Already checked in",
            ["data"] = new JsonObject()
        };

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse.ToJsonString());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);

        var service = new DailyCheckInService(m_HttpClientFactoryMock.Object, m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("already checked in").IgnoreCase);
    }

    [Test]
    public async Task CheckInAsync_UnknownError_SendsErrorMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";

        // Setup HTTP response for unknown error (some other retcode)
        var jsonResponse = new JsonObject
        {
            ["retcode"] = -1,
            ["message"] = "Error",
            ["data"] = new JsonObject()
        };

        SetupHttpResponse(HttpStatusCode.OK, jsonResponse.ToJsonString());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);

        var service = new DailyCheckInService(m_HttpClientFactoryMock.Object, m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("unknown error").IgnoreCase);
    }

    [Test]
    public async Task CheckInAsync_HttpRequestFails_HandlesErrorGracefully()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";

        // Setup HTTP response for failed request
        SetupHttpResponse(HttpStatusCode.InternalServerError, "");

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);

        var service = new DailyCheckInService(m_HttpClientFactoryMock.Object, m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("unknown error").IgnoreCase);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        m_MockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
    }

    private static IInteractionContext CreateMockInteractionContext(SlashCommandInteraction interaction)
    {
        var contextMock = new Mock<IInteractionContext>();
        contextMock.SetupGet(x => x.Interaction).Returns(interaction);
        return contextMock.Object;
    }
}