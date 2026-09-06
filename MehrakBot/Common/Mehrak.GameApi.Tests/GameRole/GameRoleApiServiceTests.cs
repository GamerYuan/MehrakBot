using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.GameRole;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GameRoleApiServiceTests
{
    private const string TwoProfilesJson = """
        {"retcode":0,"message":"OK","data":{"list":[
          {"game_biz":"hk4e_global","region":"os_asia","game_uid":"800000001","nickname":"GenshinPlayer","level":60},
          {"game_biz":"hkrpg_global","region":"prod_official_asia","game_uid":"700000001","nickname":"HsrPlayer","level":70}
        ]}}
        """;

    private readonly FakeHttpMessageHandler m_Handler = new();
    private readonly Mock<ICacheService> m_Cache = new();

    private GameRoleApiService CreateService()
    {
        return new GameRoleApiService(m_Handler.ToHttpClientFactory(), m_Cache.Object,
            new Mock<ILogger<GameRoleApiService>>().Object);
    }

    [Test]
    public async Task GetAllGameProfilesAsync_RetcodeZero_ReturnsAllMappedProfiles()
    {
        m_Handler.EnqueueJson(TwoProfilesJson);
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Has.Count.EqualTo(2));
            Assert.That(result.Data![0].Game, Is.EqualTo(Game.Genshin));
            Assert.That(result.Data[0].Region, Is.EqualTo("Asia"));
            Assert.That(result.Data[0].Profile.Nickname, Is.EqualTo("GenshinPlayer"));
            Assert.That(result.Data[0].Profile.Level, Is.EqualTo(60));
            Assert.That(result.Data[1].Game, Is.EqualTo(Game.HonkaiStarRail));
            Assert.That(result.Data[1].Region, Is.EqualTo("Asia"));
        });
        m_Cache.Verify(
            c => c.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == "gameProfile:1:100"),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllGameProfilesAsync_CacheHit_DoesNotCallApi()
    {
        m_Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoProfilesJson);
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Has.Count.EqualTo(2));
            Assert.That(m_Handler.RequestCount, Is.Zero);
        });
    }

    [Test]
    public async Task GetAllGameProfilesAsync_BypassCache_CallsApiDespiteWarmCache()
    {
        m_Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoProfilesJson);
        m_Handler.EnqueueJson(TwoProfilesJson);
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken", bypassCache: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Has.Count.EqualTo(2));
            Assert.That(m_Handler.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetAllGameProfilesAsync_RetCodeNegative100_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson("""{"retcode":-100,"message":"auth error"}""");
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("Invalid HoYoLAB UID or Cookies"));
        });
    }

    [Test]
    public async Task GetAllGameProfilesAsync_UnknownRetcode_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson("""{"retcode":1234,"message":"unknown"}""");
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }

    [Test]
    public async Task GetAllGameProfilesAsync_EmptyList_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"list":[]}}""");
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Does.Contain("No game information found"));
        });
    }

    [Test]
    public async Task GetAllGameProfilesAsync_NonSuccessStatusCode_ReturnsExternalServerError()
    {
        m_Handler.Enqueue(() => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var service = CreateService();

        var result = await service.GetAllGameProfilesAsync(1, 100, "ltoken");

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }

    [Test]
    public async Task GetAsync_CacheHit_ReturnsProfileMatchingContext()
    {
        m_Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoProfilesJson);
        var service = CreateService();
        var context = new GameRoleApiContext(1, 100, "ltoken", Game.Genshin, "os_asia");

        var result = await service.GetAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Nickname, Is.EqualTo("GenshinPlayer"));
            Assert.That(result.Data.GameUid, Is.EqualTo("800000001"));
            Assert.That(m_Handler.RequestCount, Is.Zero);
        });
    }

    /// <summary>
    /// Finding 5: a malformed credential must be rejected with a sanitized
    /// failure before any Cookie header is constructed. No HTTP request may be
    /// sent, and neither the token canary nor any exception text containing it
    /// may reach the logs.
    /// </summary>
    private sealed class CapturingLogger : ILogger<GameRoleApiService>
    {
        public readonly List<string> Entries = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            if (exception is not null)
                Entries.Add(exception.ToString());
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private GameRoleApiService CreateService(CapturingLogger logger)
    {
        return new GameRoleApiService(m_Handler.ToHttpClientFactory(), m_Cache.Object, logger);
    }

    [Test]
    [TestCase("C4N4RY\u0001TOKEN", Description = "Control character: the audit's confirmed leak trigger")]
    [TestCase("C4N4RY;TOKEN")]
    [TestCase("C4N4RY TOKEN")]
    public async Task GetAllGameProfilesAsync_MalformedLToken_ReturnsUnauthorizedWithoutLoggingCanary(string ltoken)
    {
        var logger = new CapturingLogger();
        var service = CreateService(logger);

        var result = await service.GetAllGameProfilesAsync(1, 100, ltoken, bypassCache: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Not.Contain("C4N4RY"));
            Assert.That(m_Handler.RequestCount, Is.Zero);
            Assert.That(logger.Entries, Is.Not.Empty);
            Assert.That(logger.Entries.Any(e => e.Contains("C4N4RY", StringComparison.Ordinal)), Is.False,
                "Token canary must never appear in logs");
        });
    }

    [Test]
    public async Task GetAllGameProfilesAsync_OverlongLToken_ReturnsUnauthorizedWithoutLoggingCanary()
    {
        var canaryToken = "C4N4RY" + new string('a', 4096);
        var logger = new CapturingLogger();
        var service = CreateService(logger);

        var result = await service.GetAllGameProfilesAsync(1, 100, canaryToken, bypassCache: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(m_Handler.RequestCount, Is.Zero);
            Assert.That(logger.Entries.Any(e => e.Contains("C4N4RY", StringComparison.Ordinal)), Is.False,
                "Token canary must never appear in logs");
        });
    }

    [Test]
    public async Task GetAsync_MalformedLToken_ReturnsUnauthorizedWithoutLoggingCanary()
    {
        var logger = new CapturingLogger();
        var service = CreateService(logger);
        var context = new GameRoleApiContext(1, 100, "C4N4RY\u0001TOKEN", Game.Genshin, "os_asia");

        var result = await service.GetAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(m_Handler.RequestCount, Is.Zero);
            Assert.That(logger.Entries.Any(e => e.Contains("C4N4RY", StringComparison.Ordinal)), Is.False,
                "Token canary must never appear in logs");
        });
    }
}
