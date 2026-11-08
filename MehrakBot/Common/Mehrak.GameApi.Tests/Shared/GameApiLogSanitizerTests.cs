#region

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.CodeRedeem;
using Mehrak.GameApi.DailyCheckIn;
using Mehrak.GameApi.GameRecord;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Mehrak.GameApi.Wiki;
using Mehrak.GameApi.Zzz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

#endregion

namespace Mehrak.GameApi.Tests.Shared;

/// <summary>
/// Regression matrix for redacted failure diagnostics: request query/cookie/header erasure, known-model body
/// sanitization, opaque-shape omission, bounded rendering, local legacy log replacement and real sink coverage.
/// </summary>
[TestFixture]
public class GameApiLogSanitizerTests
{
    private const string SensitiveCanary = "SENSITIVE-CANARY";
    private const string UidCanary = "UID-CANARY";
    private const string NicknameCanary = "NICKNAME-CANARY";
    private const string CodeCanary = "CDKEY-CANARY";
    private const string CookieCanary = "COOKIE-CANARY";
    private const string DsCanary = "DS-SIGNATURE-CANARY";

    [Test]
    public void Endpoint_RemovesQueryValuesUserInfoAndFragment()
    {
        var endpoint = GameApiLog.Endpoint(
            $"https://user:pass@example.invalid/path/to?cdkey={CodeCanary}&uid={UidCanary}&entry_page_id=123#fragment");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint, Is.EqualTo("https://example.invalid/path/to"));
            Assert.That(endpoint, Does.Not.Contain(CodeCanary));
            Assert.That(endpoint, Does.Not.Contain(UidCanary));
            Assert.That(endpoint, Does.Not.Contain("user:pass"));
            Assert.That(endpoint, Does.Not.Contain("fragment"));
            Assert.That(GameApiLog.Endpoint("not a uri"), Is.EqualTo(GameApiLog.InvalidUriMarker));
            Assert.That(GameApiLog.Endpoint(null), Is.EqualTo(GameApiLog.UnavailableMarker));
        }
    }

    [Test]
    public void SafeQueryParameters_RetainsOnlyValidatedUniqueEntryPageId()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=123"),
                Is.EqualTo("entry_page_id=123"));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?Entry_Page_ID=7"),
                Is.EqualTo("entry_page_id=7"));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=123&entry_page_id=456"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=%31%32%33"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=abc"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=1234567890123"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
            Assert.That(GameApiLog.SafeQueryParameters("https://example.invalid/w?entry_page_id=12%0d%0a"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
            Assert.That(
                GameApiLog.SafeQueryParameters($"https://example.invalid/w?cdkey={CodeCanary}&uid={UidCanary}"),
                Is.EqualTo(GameApiLog.NoSafeQueryParameters));
        }
    }

    [Test]
    public void RenderBody_AnnotatedModelErasesClassifiedAndOpaqueMembers()
    {
        var model = new AnnotatedFixture
        {
            Secret = SensitiveCanary,
            Level = 7,
            Name = "SAFE-NAME",
            SourceName = "RENAMED",
            Ignored = "IGNORED-CANARY",
            Opaque = new { Value = SensitiveCanary },
            Node = JsonNode.Parse($$$"""{"value":"{{{SensitiveCanary}}}"}"""),
            Map = new Dictionary<string, string> { ["key"] = SensitiveCanary },
            Entries = [new AnnotatedEntry { Uid = UidCanary, Label = "SAFE-ENTRY" }]
        };

        var rendered = GameApiLog.RenderBody(model);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rendered, Does.Not.Contain(SensitiveCanary));
            Assert.That(rendered, Does.Not.Contain(UidCanary));
            Assert.That(rendered, Does.Not.Contain("IGNORED-CANARY"));
            Assert.That(rendered, Does.Contain("\"level\":7"));
            Assert.That(rendered, Does.Contain("\"name\":\"SAFE-NAME\""));
            Assert.That(rendered, Does.Contain("\"renamed\":\"RENAMED\""));
            Assert.That(rendered, Does.Contain("\"label\":\"SAFE-ENTRY\""));
            Assert.That(rendered, Does.Contain(GameApiLog.OmittedMarker));
        }
    }

    [Test]
    public void RenderBody_UnmodeledShapesAreOmittedWithoutRawFallback()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GameApiLog.RenderBody(null), Is.EqualTo(GameApiLog.OmittedMarker));
            Assert.That(GameApiLog.RenderBody(new object()), Is.EqualTo(GameApiLog.OmittedMarker));
            Assert.That(GameApiLog.RenderBody(JsonNode.Parse($$$"""{"value":"{{{SensitiveCanary}}}"}""")),
                Is.EqualTo(GameApiLog.OmittedMarker));
            Assert.That(GameApiLog.RenderBody(new Dictionary<string, string> { ["value"] = SensitiveCanary }),
                Is.EqualTo(GameApiLog.OmittedMarker));
            Assert.That(GameApiLog.RenderBody(new InvalidOperationException(SensitiveCanary)),
                Is.EqualTo(GameApiLog.OmittedMarker));
            Assert.That(GameApiLog.RenderBody(new byte[] { 1, 2, 3 }), Is.EqualTo(GameApiLog.OmittedMarker));
        }
    }

    [Test]
    public void RenderBody_FailsSafeOnCyclesLimitsAndThrowingGetters()
    {
        var cycle = new CyclicFixture { Name = "SAFE" };
        cycle.Next = cycle;

        var large = new LargeFixture
        {
            Values = Enumerable.Range(0, 50).ToList(),
            Text = new string('x', 10000)
        };

        var cycleRendered = GameApiLog.RenderBody(cycle);
        var largeRendered = GameApiLog.RenderBody(large);
        var throwingRendered = GameApiLog.RenderBody(new ThrowingFixture());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cycleRendered, Does.Contain("<omitted>").Or.Contain("<truncated>"));
            Assert.That(largeRendered.Length, Is.LessThanOrEqualTo(GameApiLogBodyRenderer.MaxRenderedChars));
            Assert.That(largeRendered, Does.Contain(GameApiLogBodyRenderer.TruncatedMarker));
            Assert.That(throwingRendered, Does.Contain(GameApiLog.OmittedMarker));
        }
    }

    [Test]
    public async Task GameRoleUnknownRetcode_ErasesAccountIdentifiersAndKeepsSafeFields()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"list":[
              {"game_biz":"hk4e_global","region":"os_asia","game_uid":"{{{UidCanary}}}",
               "nickname":"{{{NicknameCanary}}}","level":60,"is_chosen":true,
               "region_name":"Asia","is_official":true}
            ]}}
            """);

        var logger = new CapturingLogger<GameRoleApiService>();
        var service = new GameRoleApiService(handler.ToHttpClientFactory(), cache.Object, logger);

        var result = await service.GetAsync(
            new GameRoleApiContext(123456789, 987654321, "synthetic-token", Game.Genshin, "os_asia"));

        var stateValues = logger.States.SelectMany(entry => entry).Select(pair => pair.Value).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(stateValues.Any(value => value is string text &&
                                                 (text.Contains(UidCanary, StringComparison.Ordinal) ||
                                                  text.Contains(NicknameCanary, StringComparison.Ordinal) ||
                                                  text.Contains(SensitiveCanary, StringComparison.Ordinal))), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"region\":\"os_asia\"", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("987654321", StringComparison.Ordinal)), Is.False,
                "The HoYoLAB account id must not be logged");
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False,
                "The bot user id must be erased before logging");
            Assert.That(stateValues.Any(value => value is string text && text.Contains("123456789",
                StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public async Task GameRoleEmptyList_DoesNotLogLtUid()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"list":[]}}""");

        var logger = new CapturingLogger<GameRoleApiService>();
        var service = new GameRoleApiService(handler.ToHttpClientFactory(), cache.Object, logger);
        await service.GetAsync(new GameRoleApiContext(123456789, 987654321, "synthetic-token", Game.Genshin, "os_asia"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries.Any(entry => entry.Contains("987654321", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.States.SelectMany(entry => entry).Any(pair => pair.Key == "UserId" &&
                Equals(pair.Value, GameApiLog.RedactedMarker)), Is.True);
            Assert.That(logger.Entries, Is.Not.Empty);
        }
    }

    [Test]
    public async Task GameRecordUnknownRetcode_ErasesRoleAndNicknameAndKeepsGameFields()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"list":[
              {"has_role":true,"game_id":2,"game_role_id":"{{{UidCanary}}}","nickname":"{{{NicknameCanary}}}",
               "region":"os_asia","level":60,
               "data":[{"name":"Achievements","type":1,"value":"12"}],
               "region_name":"Asia","game_name":"Genshin Impact"}
            ]}}
            """);

        var logger = new CapturingLogger<GameRecordApiService>();
        var service = new GameRecordApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new GameRecordApiContext(123456789, 987654321, "synthetic-token"));

        var stateValues = logger.States.SelectMany(entry => entry).Select(pair => pair.Value).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(stateValues.Any(value => value is string text &&
                                                 (text.Contains(UidCanary, StringComparison.Ordinal) ||
                                                  text.Contains(NicknameCanary, StringComparison.Ordinal) ||
                                                  text.Contains(SensitiveCanary, StringComparison.Ordinal))), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"game_name\":\"Genshin Impact\"",
                StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"name\":\"Achievements\"", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False,
                "The bot user id must be erased before logging");
            Assert.That(stateValues.Any(value => value is string text && text.Contains("123456789",
                StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_NonSuccessStatus_KeepsSanitizedModeledBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$$"""
                {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60,
                 "list":[{"uid":"{{{UidCanary}}}","name":"SAFE-ENTRY"}]}}
                """, Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<object>();
        var requestUri = $"https://example.invalid/path?cdkey={CodeCanary}&entry_page_id=123";
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest(requestUri), CreatePolicy<DiagnosticPayload>(),
            new HoYoLabCredentials(123456789, 100, "synthetic-token"), handler.ToHttpClientFactory(), logger,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.EqualTo(requestUri), "The result URI contract remains unredacted");
            Assert.That(result.RequestUri, Does.Contain(CodeCanary));
            Assert.That(logger.Entries.Any(entry => entry.Contains(CodeCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"name\":\"SAFE-ENTRY\"", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains("entry_page_id=123", StringComparison.Ordinal)),
                Is.True);
        }
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.ProxyAuthenticationRequired)]
    public async Task ExecuteAsync_AuthStatus_DoesNotReadOrLogBody(HttpStatusCode statusCode)
    {
        var content = new ReadTrackingContent($$$"""
            {"retcode":0,"message":"{{{SensitiveCanary}}}","data":{"uid":"{{{UidCanary}}}"}}
            """);
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            ReasonPhrase = SensitiveCanary,
            Content = content
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/auth-status"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" && Equals(pair.Value, GameApiLog.NotReadMarker)),
                Is.True);
            Assert.That(content.ReadCalls, Is.Zero, "The transport must not consume an authentication body");
            Assert.That(content.BytesRead, Is.Zero);
            Assert.That(content.Disposed, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_HtmlNonSuccessBody_IsOmitted()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent($"<html><body>{SensitiveCanary}</body></html>", Encoding.UTF8, "text/html")
        });

        var logger = new CapturingLogger<object>();
        await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/html"), CreatePolicy<DiagnosticPayload>(),
            new HoYoLabCredentials(123456789, 100, "synthetic-token"), handler.ToHttpClientFactory(), logger,
            CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "ContentType" && Equals(pair.Value, "text/html")), Is.True);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" && Equals(pair.Value, GameApiLog.OmittedMarker)),
                Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_HugeNonSuccessBody_IsSizeLimited()
    {
        var hugeBody = $$$"""{"retcode":0,"message":"{{{SensitiveCanary}}}","data":"{{{new string('a', 70 * 1024)}}}"}""";
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(hugeBody, Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<object>();
        await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/huge"), CreatePolicy<DiagnosticPayload>(),
            new HoYoLabCredentials(123456789, 100, "synthetic-token"), handler.ToHttpClientFactory(), logger,
            CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.Unavailable(GameApiLogBodyReader.SizeLimitReason))),
                Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_UnknownLengthHugeNonSuccessBody_StopsAtBoundedRead()
    {
        var hugeBody = $$$"""{"retcode":0,"message":"{{{SensitiveCanary}}}","data":"{{{new string('a', 200 * 1024)}}}"}""";
        var content = new ReadTrackingContent(hugeBody);
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = content
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/stream-huge"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(content.BodyLength, Is.GreaterThan(GameApiLogBodyReader.MaxBodyBytes));
            Assert.That(content.BytesRead, Is.LessThanOrEqualTo(GameApiLogBodyReader.MaxBodyBytes + 1),
                "The diagnostic read must stop at the cap plus one sentinel byte");
            Assert.That(content.ReadCalls, Is.GreaterThan(0));
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.Unavailable(GameApiLogBodyReader.SizeLimitReason))),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(content.Disposed, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_NonSuccessBodyReadFailure_PreservesExternalServerError()
    {
        var content = new ReadTrackingContent("unused", throwOnRead: true,
            exceptionMessage: $"stream failure {SensitiveCanary}");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = content
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/read-failure"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError),
                "A diagnostic read failure must not override the known HTTP failure");
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized status"));
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.Unavailable(nameof(IOException)))),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False,
                "The exception message must never be logged");
            Assert.That(content.ReadCalls, Is.GreaterThan(0));
            Assert.That(content.Disposed, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_Utf16NonSuccessBody_IsDecodedForDiagnostics()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$$"""
                {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60}}
                """, Encoding.Unicode, "application/json")
        });

        var logger = new CapturingLogger<object>();
        await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/utf16-status"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_MalformedSuccessBody_OmitsRawBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{not-json {SensitiveCanary}", Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/malformed"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" && Equals(pair.Value, GameApiLog.OmittedMarker)),
                Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_AuthRetcode_LogsNoResponseBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":10001,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":1,"list":[]}}
            """);

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/auth"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody"), Is.False);
        }
    }

    [Test]
    public async Task CodeRedeemSuccess_DoesNotLogCodeGameUidOrCredentials()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":null}""");
        var logger = new CapturingLogger<CodeRedeemApiService>();
        var service = new CodeRedeemApiService(handler.ToHttpClientFactory(), logger);

        var result = await service.GetAsync(new CodeRedeemApiContext(123456789, 987654321, "synthetic-token",
            "800000001", "os_asia", Game.Genshin, CodeCanary));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.RequestUri, Does.Contain(CodeCanary));
            Assert.That(result.RequestUri, Does.Contain("800000001"));
            Assert.That(logger.Entries.Any(entry => entry.Contains(CodeCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("800000001", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("987654321", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("webExchangeCdkeyHyl", StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task CodeRedeemUnknownRetcode_DoesNotLogPayloadCanaries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":12345,"message":"{{{SensitiveCanary}}}","data":{"canary":"{{{UidCanary}}}"}}
            """);
        var logger = new CapturingLogger<CodeRedeemApiService>();
        var service = new CodeRedeemApiService(handler.ToHttpClientFactory(), logger);

        var result = await service.GetAsync(new CodeRedeemApiContext(123456789, 987654321, "synthetic-token",
            "800000001", "os_asia", Game.Genshin, CodeCanary));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(CodeCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"retcode\":12345", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains(GameApiLog.OmittedMarker, StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task DailyCheckInAlreadyCheckedIn_DoesNotLogLtUid()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":-5003,"message":"already","data":null}""");
        var logger = new CapturingLogger<DailyCheckInApiService>();
        var service = new DailyCheckInApiService(handler.ToHttpClientFactory(), logger);

        var result = await service.GetAsync(new CheckInApiContext(123456789, 987654321, "synthetic-token",
            Game.Genshin));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Data, Is.EqualTo(CheckInStatus.AlreadyCheckedIn));
            Assert.That(logger.Entries.Any(entry => entry.Contains("987654321", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.States.SelectMany(entry => entry).Any(pair => pair.Key == "UserId" &&
                Equals(pair.Value, GameApiLog.RedactedMarker)), Is.True);
        }
    }

    [Test]
    public async Task WikiJsonNodeFailure_OmitsRawPayload()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":12345,"message":"{{{SensitiveCanary}}}","data":{"name":"{{{UidCanary}}}"}}
            """);
        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);

        var result = await service.GetAsync(new WikiApiContext(123456789, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("123456789", StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("entry_page_id=123", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains(GameApiLog.OmittedMarker, StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task RequestHeadersAndCookie_AreNeverLogged()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""{"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"level":1}}""");

        var logger = new CapturingLogger<object>();
        await HoYoLabApi.ExecuteAsync(
            CreateRequest("https://example.invalid/headers",
                [new KeyValuePair<string, string>("DS", DsCanary)]),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(123456789, 100, CookieCanary),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logger.Entries.Any(entry => entry.Contains(CookieCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(DsCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public async Task RealSerilogSink_DirectConstruction_ReceivesNoSensitiveValues()
    {
        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"list":[
              {"game_biz":"hk4e_global","region":"os_asia","game_uid":"{{{UidCanary}}}",
               "nickname":"{{{NicknameCanary}}}","level":60}
            ]}}
            """);

        var service = new GameRoleApiService(handler.ToHttpClientFactory(), cache.Object,
            factory.CreateLogger<GameRoleApiService>());
        var result = await service.GetAsync(
            new GameRoleApiContext(123456789, 987654321, "synthetic-token", Game.Genshin, "os_asia"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(sink.ContainsAny([SensitiveCanary, UidCanary, NicknameCanary, "987654321", "123456789"]),
                Is.False);
            Assert.That(sink.ContainsJson("\"level\":60"), Is.True);
        }
    }

    [Test]
    public async Task RealSerilogSink_DiRegistration_ReceivesNoSensitiveValues()
    {
        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"list":[
              {"has_role":true,"game_id":2,"game_role_id":"{{{UidCanary}}}","nickname":"{{{NicknameCanary}}}",
               "region":"os_asia","level":60,"data":[],"region_name":"Asia","game_name":"Genshin Impact"}
            ]}}
            """);

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: false));
        services.AddSingleton(handler.ToHttpClientFactory());
        services.AddSingleton(cache.Object);
        services.AddGameApiServices();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>>();
        var result = await service.GetAsync(new GameRecordApiContext(123456789, 987654321, "synthetic-token"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(sink.ContainsAny([SensitiveCanary, UidCanary, NicknameCanary, "987654321", "123456789"]),
                Is.False);
            Assert.That(sink.ContainsJson("\"game_name\":\"Genshin Impact\""), Is.True);
        }
    }

    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task ZzzDefenseFailure_ErasesNicknameAndRetainsSafeFields(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler();
        var body = $$$"""
            {"retcode":12345,"message":"{{{SensitiveCanary}}}","data":{"hadal_ver":"1","hadal_info_v2":null,"nick_name":"{{{NicknameCanary}}}","icon":"x"}}
            """;
        handler.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<ZzzDefenseApiService>();
        var service = new ZzzDefenseApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new BaseHoYoApiContext(123456789, 987654321, "synthetic-token",
            "800000001", "prod_official_asia"));

        var stateValues = logger.States.SelectMany(entry => entry).Select(pair => pair.Value).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)),
                Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)),
                Is.False);
            Assert.That(stateValues.Any(value => value is string text &&
                                                 (text.Contains(NicknameCanary, StringComparison.Ordinal) ||
                                                  text.Contains(SensitiveCanary, StringComparison.Ordinal))), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"hadal_ver\":\"1\"", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logger.Entries.Any(entry => entry.Contains(GameApiLog.RedactedMarker, StringComparison.Ordinal)),
                Is.True);
        }
    }

    [TestCase(10001)]
    [TestCase(-100)]
    public async Task ExecuteAsync_NonSuccessAuthEnvelope_SuppressesBody(int authRetcode)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$$"""
                {"retcode":{{{authRetcode}}},"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60,"list":[]}}
                """, Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/auth-non2xx"),
            CreatePolicy<DiagnosticPayload>(authRetcode), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.AuthBodySuppressedMarker)), Is.True);
        }
    }

    [TestCase(10001)]
    [TestCase(-100)]
    public async Task ExecuteAsync_TypedParseFailureAuthEnvelope_SuppressesFallbackBody(int authRetcode)
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":{{{authRetcode}}},"message":"{{{SensitiveCanary}}}","data":{"unexpected":"{{{UidCanary}}}"}}
            """);

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/auth-parse-failure"),
            CreatePolicy<DiagnosticPayload>(authRetcode), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized parse"));
            Assert.That(logger.Entries.Any(entry => entry.Contains(SensitiveCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains(UidCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.AuthBodySuppressedMarker)), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_NonSuccessAuthEnvelope_AnonymousPolicyRendersSafeModeledBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$$"""
                {"retcode":10001,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60,"list":[]}}
                """, Encoding.UTF8, "application/json")
        });

        var logger = new CapturingLogger<object>();
        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/anonymous-non2xx"),
            CreatePolicy<DiagnosticPayload>(null), new HoYoLabCredentials(123456789, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), logger, CancellationToken.None);

        var state = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Entries.Any(entry => entry.Contains(NicknameCanary, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)), Is.True);
            Assert.That(state.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                          Equals(pair.Value, GameApiLog.AuthBodySuppressedMarker)), Is.False);
        }
    }

    [Test]
    public async Task UserIdContext_IsErasedAcrossSharedLocalExceptionAndSerilogSink()
    {
        const ulong userIdCanary = 918273645091827;
        const string exceptionCanary = "EXCEPTION-MESSAGE-CANARY";
        var userIdText = userIdCanary.ToString(CultureInfo.InvariantCulture);
        const string safeQuery = "entry_page_id=123";

        var sharedHandler = new FakeHttpMessageHandler();
        sharedHandler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60,"list":[]}}
            """);
        var capturing = new CapturingLogger<object>();
        var sharedResult = await HoYoLabApi.ExecuteAsync(
            CreateRequest($"https://example.invalid/path?{safeQuery}&cdkey={CodeCanary}"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(userIdCanary, 100, "synthetic-token"),
            sharedHandler.ToHttpClientFactory(), capturing, CancellationToken.None);

        var localHandler = new FakeHttpMessageHandler();
        localHandler.EnqueueJson("""{"retcode":0,"message":"OK","data":null}""");
        var localLogger = new CapturingLogger<CodeRedeemApiService>();
        var localService = new CodeRedeemApiService(localHandler.ToHttpClientFactory(), localLogger);
        var localResult = await localService.GetAsync(new CodeRedeemApiContext(userIdCanary, 987654321,
            "synthetic-token", "800000001", "os_asia", Game.Genshin, CodeCanary));

        GameApiLog.LogException(capturing, $"https://example.invalid/path?{safeQuery}", userIdCanary,
            new InvalidOperationException(exceptionCanary));

        var sharedState = capturing.States.SelectMany(entry => entry).ToList();
        var sharedValues = sharedState.Select(pair => pair.Value).ToList();
        var localState = localLogger.States.SelectMany(entry => entry).ToList();
        var localValues = localState.Select(pair => pair.Value).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sharedResult.RequestUri, Does.Contain(CodeCanary),
                "Result.RequestUri remains the unredacted wire URI");
            Assert.That(localResult.RequestUri, Does.Contain(CodeCanary));
            Assert.That(capturing.Entries.Any(entry => entry.Contains(userIdText, StringComparison.Ordinal)),
                Is.False);
            Assert.That(sharedValues.Any(value => value is string text &&
                                                 text.Contains(userIdText, StringComparison.Ordinal)), Is.False);
            Assert.That(localLogger.Entries.Any(entry => entry.Contains(userIdText, StringComparison.Ordinal)),
                Is.False);
            Assert.That(localValues.Any(value => value is string text &&
                                                text.Contains(userIdText, StringComparison.Ordinal)), Is.False);
            Assert.That(sharedState.Any(pair => pair.Key == "UserId" &&
                                                Equals(pair.Value, GameApiLog.RedactedMarker)), Is.True);
            Assert.That(localState.Any(pair => pair.Key == "UserId" &&
                                               Equals(pair.Value, GameApiLog.RedactedMarker)), Is.True);
            Assert.That(capturing.Entries.Any(entry => entry.Contains(safeQuery, StringComparison.Ordinal)), Is.True);
            Assert.That(capturing.Entries.Any(entry => entry.Contains("\"level\":60", StringComparison.Ordinal)),
                Is.True);
            Assert.That(capturing.Entries.Any(entry => entry.Contains("\"retcode\":1234", StringComparison.Ordinal)),
                Is.True, "Safe numeric retcodes remain visible");
            Assert.That(capturing.Entries.Any(entry => entry.Contains(exceptionCanary, StringComparison.Ordinal)),
                Is.False);
            Assert.That(capturing.Entries.Any(entry =>
                entry.Contains("InvalidOperationException", StringComparison.Ordinal)), Is.True);
        }

        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        var serilogLogger = factory.CreateLogger<object>();

        var sinkHandler = new FakeHttpMessageHandler();
        sinkHandler.EnqueueJson($$$"""
            {"retcode":1234,"message":"{{{SensitiveCanary}}}","data":{"nickname":"{{{NicknameCanary}}}","level":60,"list":[]}}
            """);
        await HoYoLabApi.ExecuteAsync(
            CreateRequest($"https://example.invalid/path?{safeQuery}&cdkey={CodeCanary}"),
            CreatePolicy<DiagnosticPayload>(), new HoYoLabCredentials(userIdCanary, 100, "synthetic-token"),
            sinkHandler.ToHttpClientFactory(), serilogLogger, CancellationToken.None);

        var sinkLocalHandler = new FakeHttpMessageHandler();
        sinkLocalHandler.EnqueueJson("""{"retcode":0,"message":"OK","data":null}""");
        var sinkLocalService = new CodeRedeemApiService(sinkLocalHandler.ToHttpClientFactory(),
            factory.CreateLogger<CodeRedeemApiService>());
        await sinkLocalService.GetAsync(new CodeRedeemApiContext(userIdCanary, 987654321, "synthetic-token",
            "800000001", "os_asia", Game.Genshin, CodeCanary));

        GameApiLog.LogException(serilogLogger, "https://example.invalid/path", userIdCanary,
            new InvalidOperationException(exceptionCanary));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sink.Contains(userIdText), Is.False);
            Assert.That(sink.Contains(SensitiveCanary), Is.False);
            Assert.That(sink.Contains(exceptionCanary), Is.False);
            Assert.That(sink.Contains(GameApiLog.RedactedMarker), Is.True);
            Assert.That(sink.ContainsJson("\"level\":60"), Is.True);
            Assert.That(sink.Contains(safeQuery), Is.True);
        }
    }

    private static HoYoLabRequest CreateRequest(string requestUri,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null)
    {
        return new HoYoLabRequest
        {
            RequestUri = requestUri,
            Headers = headers ?? [],
            Failures = new HoYoLabFailureMessages
            {
                Unauthorized = "sanitized unauthorized",
                Parse = "sanitized parse",
                NonSuccessStatus = "sanitized status"
            }
        };
    }

    private static HoYoLabEnvelopePolicy<T> CreatePolicy<T>(
        int? authRetcode = HoYoLabRetcodes.GameRecordAuth) where T : class
    {
        return new HoYoLabEnvelopePolicy<T>
        {
            AuthRetcode = authRetcode,
            UnknownRetcode = "sanitized unknown retcode"
        };
    }

    private sealed class AnnotatedFixture
    {
        [GameApiSensitive]
        [JsonPropertyName("secret")]
        public required string Secret { get; init; }

        [JsonPropertyName("level")] public int Level { get; init; }

        [JsonPropertyName("name")] public required string Name { get; init; }

        [JsonPropertyName("renamed")] public required string SourceName { get; init; }

        [JsonIgnore] public required string Ignored { get; init; }

        public object? Opaque { get; init; }

        public JsonNode? Node { get; init; }

        public Dictionary<string, string> Map { get; init; } = [];

        public List<AnnotatedEntry> Entries { get; init; } = [];
    }

    private sealed class AnnotatedEntry
    {
        [GameApiSensitive]
        [JsonPropertyName("uid")]
        public required string Uid { get; init; }

        [JsonPropertyName("label")] public required string Label { get; init; }
    }

    private sealed class DiagnosticPayload
    {
        [JsonPropertyName("nickname")][GameApiSensitive] public required string Nickname { get; init; }

        [JsonPropertyName("level")] public int Level { get; init; }

        [JsonPropertyName("list")] public List<DiagnosticEntry> List { get; init; } = [];
    }

    private sealed class DiagnosticEntry
    {
        [JsonPropertyName("uid")][GameApiSensitive] public required string Uid { get; init; }

        [JsonPropertyName("name")] public required string Name { get; init; }
    }

    private sealed class CyclicFixture
    {
        public string Name { get; init; } = string.Empty;

        public CyclicFixture? Next { get; set; }
    }

    private sealed class LargeFixture
    {
        public List<int> Values { get; init; } = [];

        public string Text { get; init; } = string.Empty;
    }

    private sealed class ThrowingFixture
    {
        public string Value => throw new InvalidOperationException(SensitiveCanary);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public List<IReadOnlyList<KeyValuePair<string, object?>>> States { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            if (exception is not null)
                Entries.Add(exception.ToString());
            if (state is IReadOnlyList<KeyValuePair<string, object?>> structured)
                States.Add(structured);
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> m_Entries = [];

        public void Emit(LogEvent logEvent)
        {
            m_Entries.Add(logEvent.RenderMessage());
            foreach (var property in logEvent.Properties)
                m_Entries.Add($"{property.Key}={property.Value}");
            if (logEvent.Exception is not null)
                m_Entries.Add(logEvent.Exception.ToString());
        }

        public bool Contains(string value)
        {
            return m_Entries.Any(entry => entry.Contains(value, StringComparison.Ordinal));
        }

        public bool ContainsJson(string value)
        {
            return m_Entries.Any(entry => entry.Replace("\\\"", "\"", StringComparison.Ordinal)
                .Contains(value, StringComparison.Ordinal));
        }

        public bool ContainsAny(IEnumerable<string> values)
        {
            return values.Any(Contains);
        }
    }

    /// <summary>
    /// Streaming content with unknown length that records every body read. It never buffers on its own, so a read
    /// count of zero proves the transport did not consume the body.
    /// </summary>
    private sealed class ReadTrackingContent : HttpContent
    {
        private readonly byte[] m_Buffer;
        private readonly bool m_ThrowOnRead;
        private readonly string m_ExceptionMessage;

        public ReadTrackingContent(string body, bool throwOnRead = false, string? exceptionMessage = null)
        {
            m_Buffer = Encoding.UTF8.GetBytes(body);
            m_ThrowOnRead = throwOnRead;
            m_ExceptionMessage = exceptionMessage ?? "stream read failure";
            Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        }

        public int ReadCalls { get; private set; }

        public long BytesRead { get; private set; }

        public int BodyLength => m_Buffer.Length;

        public bool Disposed { get; private set; }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return new TrackingStream(this).CopyToAsync(stream);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new TrackingStream(this));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        private sealed class TrackingStream : Stream
        {
            private readonly ReadTrackingContent m_Owner;
            private int m_Position;

            public TrackingStream(ReadTrackingContent owner)
            {
                m_Owner = owner;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => m_Position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return Read(buffer.AsSpan(offset, count));
            }

            public override int Read(Span<byte> buffer)
            {
                m_Owner.ReadCalls++;

                if (m_Owner.m_ThrowOnRead)
                    throw new IOException(m_Owner.m_ExceptionMessage);

                var remaining = m_Owner.m_Buffer.Length - m_Position;
                var toCopy = Math.Min(buffer.Length, remaining);
                if (toCopy <= 0)
                    return 0;

                m_Owner.m_Buffer.AsSpan(m_Position, toCopy).CopyTo(buffer);
                m_Position += toCopy;
                m_Owner.BytesRead += toCopy;
                return toCopy;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<int>(cancellationToken)
                    : ValueTask.FromResult(Read(buffer.Span));
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
