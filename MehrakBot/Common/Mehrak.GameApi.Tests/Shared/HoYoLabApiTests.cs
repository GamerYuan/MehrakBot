using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Telemetry;
using Mehrak.GameApi.CodeRedeem;
using Mehrak.GameApi.GameRecord;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Hoyolab;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Mehrak.GameApi.Wiki;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Shared;

/// <summary>
/// Regression matrix for the shared HoYoLAB request pipeline: wire shape, credential safety, uniform retcode/data
/// precedence, controlled error copy, charset-aware parsing, cancellation and disposal.
/// </summary>
[TestFixture]
public class HoYoLabApiTests
{
    private const string SuccessJson = """{"retcode":0,"message":"OK","data":{"value":7}}""";
    private const string UpstreamCanary = "UPSTREAM-CANARY-TEXT";
    private const string DefaultUnauthorized =
        "Invalid HoYoLAB UID or Cookies. Please authenticate again.";
    private const string DefaultRequestFailure =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private static HoYoLabRequest CreateRequest(string requestUri,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null, bool requiresCredentials = true,
        string cookieFormat = "ltuid_v2={0}; ltoken_v2={1}")
    {
        return new HoYoLabRequest
        {
            RequestUri = requestUri,
            Headers = headers ?? [],
            RequiresCredentials = requiresCredentials,
            CookieFormat = cookieFormat,
            Failures = new HoYoLabFailureMessages
            {
                Unauthorized = "sanitized unauthorized",
                Parse = "sanitized parse",
                NonSuccessStatus = "sanitized status"
            }
        };
    }

    private static HoYoLabRequest CreateDefaultFailureRequest(string requestUri)
    {
        return new HoYoLabRequest { RequestUri = requestUri };
    }

    private static HoYoLabEnvelopePolicy<TestPayload> CreatePolicy(
        int? authRetcode = HoYoLabRetcodes.GameRecordAuth)
    {
        return new HoYoLabEnvelopePolicy<TestPayload>
        {
            AuthRetcode = authRetcode,
            UnknownRetcode = "sanitized unknown retcode"
        };
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Test]
    public async Task ExecuteAsync_GetWithCredentials_SendsCookieAndHeadersWithRequestUri()
    {
        const string requestUri = "https://example.invalid/event/api?role_id=900&server=os_asia";
        var handler = new FakeHttpMessageHandler();
        string? cookie = null;
        string? language = null;
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.ExistenceProbe;
        handler.Enqueue(request =>
        {
            observedKind = ExpectedDependency.Current;
            cookie = request.Headers.GetValues("Cookie").Single();
            language = request.Headers.GetValues("X-Rpc-Language").Single();
            return JsonResponse(SuccessJson);
        });

        var result = await HoYoLabApi.ExecuteAsync(
            CreateRequest(requestUri, [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Value, Is.EqualTo(7));
            Assert.That(result.RequestUri, Is.EqualTo(requestUri));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.LastRequest.RequestUri!.ToString(), Is.EqualTo(requestUri));
            Assert.That(cookie, Is.EqualTo("ltuid_v2=100; ltoken_v2=synthetic-token"));
            Assert.That(language, Is.EqualTo("en-us"));
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.None));
        }
    }

    [Test]
    public async Task ExecuteAsync_PostWithBody_SendsJsonBodyAndAddsCookieBeforeHeaders()
    {
        var handler = new FakeHttpMessageHandler();
        string? body = null;
        handler.Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(SuccessJson);
        });

        var request = CreateRequest("https://example.invalid/event/post",
            [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]) with
        {
            Method = HttpMethod.Post,
            Body = new TestPostPayload { RoleId = "900000001", Server = "os_asia", SortType = 1 }
        };

        var result = await HoYoLabApi.ExecuteAsync(request, CreatePolicy(),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));

        using var document = JsonDocument.Parse(body!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.RootElement.GetProperty("role_id").GetString(), Is.EqualTo("900000001"));
            Assert.That(document.RootElement.GetProperty("server").GetString(), Is.EqualTo("os_asia"));
            Assert.That(document.RootElement.GetProperty("sort_type").GetInt32(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_Anonymous_DoesNotSendCookieHeader()
    {
        var handler = new FakeHttpMessageHandler();
        bool hasCookie = true;
        handler.Enqueue(request =>
        {
            hasCookie = request.Headers.Contains("Cookie");
            return JsonResponse(SuccessJson);
        });

        var result = await HoYoLabApi.ExecuteAsync(
            CreateRequest("https://example.invalid/anon", requiresCredentials: false),
            CreatePolicy(authRetcode: null), new HoYoLabCredentials(1, 0, string.Empty),
            handler.ToHttpClientFactory(), new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(hasCookie, Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_CustomCookieFormat_IsSentVerbatim()
    {
        var handler = new FakeHttpMessageHandler();
        string? cookie = null;
        handler.Enqueue(request =>
        {
            cookie = request.Headers.GetValues("Cookie").Single();
            return JsonResponse(SuccessJson);
        });

        await HoYoLabApi.ExecuteAsync(
            CreateRequest("https://example.invalid/cookie", cookieFormat: "ltoken_v2={1}; ltuid_v2={0};"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        Assert.That(cookie, Is.EqualTo("ltoken_v2=synthetic-token; ltuid_v2=100;"));
    }

    [Test]
    public async Task ExecuteAsync_AuthRetcodeWithoutData_ReturnsUnauthorizedBeforeDataValidation()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":10001,"message":"auth error"}""");

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/auth-no-data"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized unauthorized"));
            Assert.That(result.RequestUri, Is.EqualTo("https://example.invalid/auth-no-data"));
        }
    }

    [Test]
    public async Task ExecuteAsync_AccountAuthRetcode_IsSelectedByPolicy()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":-100,"message":"auth error"}""");

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/account-auth"),
            CreatePolicy(HoYoLabRetcodes.AccountAuth), new HoYoLabCredentials(1, 100, "synthetic-token"),
            handler.ToHttpClientFactory(), new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized unauthorized"));
        }
    }

    [Test]
    public async Task ExecuteAsync_AnonymousWithNoAuthScheme_DoesNotMapAuthRetcodeToUnauthorized()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$"""{"retcode":10001,"message":"{{UpstreamCanary}}"}""");

        var result = await HoYoLabApi.ExecuteAsync(
            CreateRequest("https://example.invalid/anonymous", requiresCredentials: false),
            CreatePolicy(authRetcode: null), new HoYoLabCredentials(1, 0, string.Empty),
            handler.ToHttpClientFactory(), new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized unknown retcode"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
        }
    }

    [Test]
    public async Task ExecuteAsync_UnknownRetcode_WithData_ReturnsExternalServerError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":12345,"message":"upstream","data":{"value":1}}""");

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/unknown"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized unknown retcode"));
        }
    }

    [Test]
    public async Task ExecuteAsync_MalformedCredential_RejectsBeforeSendingAndDoesNotLogOrEchoToken()
    {
        const string canary = "C4N4RY\u0001TOKEN";
        var handler = new FakeHttpMessageHandler();
        var logger = new CapturingLogger<object>();

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/credential"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, canary), handler.ToHttpClientFactory(), logger,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Not.Contain("C4N4RY"));
            Assert.That(result.RequestUri, Is.EqualTo("https://example.invalid/credential"));
            Assert.That(handler.RequestCount, Is.Zero);
            Assert.That(logger.Entries, Is.Not.Empty);
            Assert.That(logger.Entries.Any(e => e.Contains("C4N4RY", StringComparison.Ordinal)), Is.False,
                "Token canary must never appear in logs");
        }
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    public async Task ExecuteAsync_NonSuccessStatus_ReturnsControlledMessageWithoutUpstreamText(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler();
        var content = new TrackingContent(SuccessJson);
        handler.Enqueue(() => new HttpResponseMessage(status)
        {
            ReasonPhrase = UpstreamCanary,
            Content = content
        });

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/status"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized status"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.RequestUri, Is.EqualTo("https://example.invalid/status"));
            Assert.That(content.Disposed, Is.True, "Non-success responses must be disposed");
        }
    }

    [Test]
    public async Task ExecuteAsync_MalformedJson_ReturnsControlledParseFailure()
    {
        var handler = new FakeHttpMessageHandler();
        var content = new TrackingContent("{this is not json");
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/malformed"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized parse"));
            Assert.That(result.ErrorMessage, Does.Not.Contain("JsonReaderException"));
            Assert.That(result.RequestUri, Is.EqualTo("https://example.invalid/malformed"));
            Assert.That(content.Disposed, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_OmittedFailures_AuthRetcodeUsesSharedUnauthorizedDefault()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":10001,"message":"auth error"}""");

        var result = await HoYoLabApi.ExecuteAsync(
            CreateDefaultFailureRequest("https://example.invalid/default-auth"), CreatePolicy(),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultUnauthorized));
        }
    }

    [Test]
    public async Task ExecuteAsync_OmittedFailures_MalformedCredentialUsesSharedUnauthorizedDefault()
    {
        var handler = new FakeHttpMessageHandler();

        var result = await HoYoLabApi.ExecuteAsync(
            CreateDefaultFailureRequest("https://example.invalid/default-credential"), CreatePolicy(),
            new HoYoLabCredentials(1, 100, "bad token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultUnauthorized));
            Assert.That(handler.RequestCount, Is.Zero);
        }
    }

    [TestCase("null")]
    [TestCase("{this is not json")]
    public async Task ExecuteAsync_OmittedFailures_ParseFailureUsesSharedDefault(string body)
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(body);

        var result = await HoYoLabApi.ExecuteAsync(
            CreateDefaultFailureRequest("https://example.invalid/default-parse"), CreatePolicy(),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
        }
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task ExecuteAsync_OmittedFailures_NonSuccessStatusUsesSharedDefault(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(status));

        var result = await HoYoLabApi.ExecuteAsync(
            CreateDefaultFailureRequest("https://example.invalid/default-status"), CreatePolicy(),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
        }
    }

    [Test]
    public async Task ExecuteAsync_InvalidPayloadShape_ReturnsControlledParseFailure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"value":"not-a-number"}}""");

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/invalid-payload"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("sanitized parse"));
        }
    }

    [Test]
    public async Task ExecuteAsync_Success_DisposesResponseContent()
    {
        var handler = new FakeHttpMessageHandler();
        var content = new TrackingContent(SuccessJson);
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/dispose-success"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(content.Disposed, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_HeaderSetupFailure_DoesNotSendRequest()
    {
        var handler = new FakeHttpMessageHandler();
        var request = CreateRequest("https://example.invalid/bad-header",
            [new KeyValuePair<string, string>("Bad-Header", "value\r\nInjected")]);

        await Assert.ThrowsAsync<FormatException>(() => HoYoLabApi.ExecuteAsync(request, CreatePolicy(),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_WebDefaults_ReadNumbersFromStrings()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":"0","message":"OK","data":{"value":"42"}}""");

        var result = await HoYoLabApi.ExecuteAsync(CreateRequest("https://example.invalid/web-options"),
            CreatePolicy(), new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Value, Is.EqualTo(42));
        }
    }

    [Test]
    public async Task SendEnvelopeAsync_DeclaredUtf16Charset_IsParsedWithSharedOptions()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessJson, Encoding.Unicode, "application/json")
        });

        var result = await HoYoLabApi.SendEnvelopeAsync<TestPayload>(
            CreateRequest("https://example.invalid/utf16"),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Data!.Value, Is.EqualTo(7));
        }
    }

    [Test]
    public async Task SendJsonAsync_DeclaredUtf16Charset_IsParsed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"retcode":0,"message":"OK","data":{"name":"Canary"}}""",
                Encoding.Unicode, "application/json")
        });

        var result = await HoYoLabApi.SendJsonAsync(CreateRequest("https://example.invalid/utf16-node"),
            new HoYoLabCredentials(1, 100, "synthetic-token"), handler.ToHttpClientFactory(),
            new Mock<ILogger>().Object, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!["data"]!["name"]!.GetValue<string>(), Is.EqualTo("Canary"));
            Assert.That(HoYoLabApi.TryReadRetcode(result.Data, out var retcode), Is.True);
            Assert.That(retcode, Is.Zero);
        }
    }

    [Test]
    public void TryReadRetcode_FailedExtraction_DoesNotReportSuccessSentinel()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HoYoLabApi.TryReadRetcode(JsonNode.Parse("""{"retcode":"invalid"}"""), out var invalid),
                Is.False);
            Assert.That(invalid, Is.Not.Zero);
            Assert.That(HoYoLabApi.TryReadRetcode(
                JsonNode.Parse("""{"retcode":"999999999999999999999999999999999999999"}"""), out var overflow),
                Is.False);
            Assert.That(overflow, Is.Not.Zero);
            Assert.That(HoYoLabApi.TryReadRetcode(JsonNode.Parse("""{"message":"OK","data":{}}"""),
                out var missing), Is.False);
            Assert.That(missing, Is.Not.Zero);
            Assert.That(HoYoLabApi.TryReadRetcode(JsonNode.Parse("""{"retcode":{"value":0}}"""),
                out var wrongShape), Is.False);
            Assert.That(wrongShape, Is.Not.Zero);
        }
    }

    [Test]
    public async Task GameRecordApiService_SendsExpectedGetContract()
    {
        const string requestUri = "https://sg-public-api.hoyolab.com/event/game_record/card/wapi/getGameRecordCard?uid=100";
        var handler = new FakeHttpMessageHandler();
        string? cookie = null;
        string? language = null;
        handler.Enqueue(request =>
        {
            cookie = request.Headers.GetValues("Cookie").Single();
            language = request.Headers.GetValues("X-Rpc-Language").Single();
            return JsonResponse("""{"retcode":0,"message":"OK","data":{"list":[]}}""");
        });

        var service = new GameRecordApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<GameRecordApiService>>().Object);
        var result = await service.GetAsync(new GameRecordApiContext(1, 100, "synthetic-token"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.LastRequest.RequestUri!.ToString(), Is.EqualTo(requestUri));
            Assert.That(cookie, Is.EqualTo("ltoken_v2=synthetic-token; ltuid_v2=100"));
            Assert.That(language, Is.EqualTo("en-us"));
            Assert.That(result.RequestUri, Is.EqualTo(requestUri));
        }
    }

    [Test]
    public async Task GameRecordApiService_AccountAuthWithoutData_ReturnsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":-100,"message":"auth"}""");

        var service = new GameRecordApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<GameRecordApiService>>().Object);
        var result = await service.GetAsync(new GameRecordApiContext(1, 100, "synthetic-token"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultUnauthorized));
        }
    }

    [Test]
    public async Task WikiApiService_SendsAnonymousRequestWithoutCookie()
    {
        var handler = new FakeHttpMessageHandler();
        bool hasCookie = true;
        handler.Enqueue(request =>
        {
            hasCookie = request.Headers.Contains("Cookie");
            return JsonResponse("""{"retcode":0,"message":"OK","data":{}}""");
        });

        var service = new WikiApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<WikiApiService>>().Object);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(hasCookie, Is.False);
            Assert.That(handler.LastRequest!.Headers.GetValues("X-Rpc-Language").Single(), Is.EqualTo("en-us"));
            Assert.That(handler.LastRequest.Headers.Contains("X-Rpc-Wiki_app"), Is.False);
        }
    }

    [Test]
    public async Task WikiApiService_HttpFailure_ReturnsControlledMessageWithoutUpstreamText()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = UpstreamCanary
        });

        var service = new WikiApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<WikiApiService>>().Object);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task WikiApiService_HttpNotFound_IsExpectedContentMissAndKeepsResultMapping()
    {
        var handler = new FakeHttpMessageHandler();
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.None;
        handler.Enqueue(_ =>
        {
            observedKind = ExpectedDependency.Current;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.WikiContentLookup));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Warning));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Error));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        }
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task WikiApiService_NonContentMissHttpFailure_RemainsAnError(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler();
        ExpectedDependencyKind observedKind = ExpectedDependencyKind.None;
        handler.Enqueue(_ =>
        {
            observedKind = ExpectedDependency.Current;
            return new HttpResponseMessage(statusCode);
        });

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(observedKind, Is.EqualTo(ExpectedDependencyKind.WikiContentLookup));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Warning));
        }
    }

    [Test]
    public async Task WikiApiService_Retcode404_IsExpectedContentMissAndDowngradesLog()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":404,"message":"not found","data":null}""");

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while accessing HoYoWiki API"));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Warning));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Error));
        }
    }

    [Test]
    public async Task WikiApiService_RetcodeOtherThan404_RemainsAnError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":12345,"message":"upstream failure","data":null}""");

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Warning));
        }
    }

    [Test]
    public async Task WikiApiService_MalformedRetcode_RemainsErrorLevel()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":"invalid","message":"upstream","data":null}""");

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Warning));
        }
    }

    [Test]
    public async Task WikiApiService_TransportFault_RemainsAnError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => throw new HttpRequestException("synthetic transport failure"));

        var logger = new CapturingLogger<WikiApiService>();
        var service = new WikiApiService(handler.ToHttpClientFactory(), logger);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.BotError));
            Assert.That(logger.Levels, Does.Contain(LogLevel.Error));
            Assert.That(logger.Levels, Does.Not.Contain(LogLevel.Warning));
        }
    }

    [TestCase("\"invalid\"")]
    [TestCase("\"999999999999999999999999999999999999999\"")]
    public async Task WikiApiService_MalformedRetcode_ReturnsControlledExternalServerError(string retcodeJson)
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            $$$"""{"retcode":{{{retcodeJson}}},"message":"{{{UpstreamCanary}}}","data":{"name":"{{{UpstreamCanary}}}"}}""");

        var service = new WikiApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<WikiApiService>>().Object);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while accessing HoYoWiki API"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task WikiApiService_StringZeroRetcode_RemainsSuccess()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":"0","message":"OK","data":{"name":"Canary"}}""");

        var service = new WikiApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<WikiApiService>>().Object);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!["data"]!["name"]!.GetValue<string>(), Is.EqualTo("Canary"));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task WikiApiService_StringAuthRetcode_IsRejectedWithoutUpstreamText()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            $$$"""{"retcode":"10001","message":"{{{UpstreamCanary}}}","data":{"name":"{{{UpstreamCanary}}}"}}""");

        var service = new WikiApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<WikiApiService>>().Object);
        var result = await service.GetAsync(new WikiApiContext(1, Game.Genshin, "123"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while accessing HoYoWiki API"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
        }
    }

    [Test]
    public async Task GenshinCharacterApiService_WarmCacheWithMalformedToken_ReturnsCacheWithoutValidation()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GenshinBasicCharacterData
                {
                    Icon = "icon.png", Name = "Cached",
                    Weapon = new Mehrak.GameApi.Genshin.Types.Weapon { Icon = "weapon.png", Name = "Weapon" }
                }
            });

        var handler = new FakeHttpMessageHandler();
        var service = new GenshinCharacterApiService(cache.Object, handler.ToHttpClientFactory(),
            new Mock<ILogger<GenshinCharacterApiService>>().Object);

        var result = await service.GetAllCharactersAsync(
            new GenshinCharacterApiContext(1, 100, "malformed token", "900000001", "os_asia"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Single().Name, Is.EqualTo("Cached"));
            Assert.That(result.RequestUri, Is.Null);
            Assert.That(handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task HylPostApiService_CustomRetcode_ReturnsControlledMessageWithoutUpstreamText()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$"""{"retcode":12345,"message":"{{UpstreamCanary}}"}""");

        var service = new HylPostApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<HylPostApiService>>().Object);
        var result = await service.GetAsync(new HylPostApiContext(1, 42));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while accessing HoYoLAB API"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.ErrorMessage, Does.Not.Contain("12345"));
        }
    }

    [Test]
    public async Task HsrCharacterApiService_SendsDsAndClientHeaderVariant()
    {
        const string avatarJson = """
            {"retcode":0,"message":"OK","data":{"avatar_list":[
              {"id":1001,"level":80,"name":"Char A","element":"Fire","icon":"a.png","image":"i.png",
               "relics":[],"ornaments":[],"ranks":[],"properties":[],"skills":[]}
            ],"equip_wiki":{},"relic_wiki":{}}}
            """;
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<IEnumerable<HsrBasicCharacterData>>(It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<HsrBasicCharacterData>?)null);

        var handler = new FakeHttpMessageHandler();
        string? ds = null;
        string? clientType = null;
        string? appVersion = null;
        handler.Enqueue(request =>
        {
            ds = request.Headers.GetValues("DS").Single();
            clientType = request.Headers.GetValues("X-Rpc-Client_type").Single();
            appVersion = request.Headers.GetValues("X-Rpc-App_version").Single();
            return JsonResponse(avatarJson);
        });

        var service = new HsrCharacterApiService(handler.ToHttpClientFactory(), cache.Object,
            new Mock<ILogger<HsrCharacterApiService>>().Object);
        var result = await service.GetAllCharactersAsync(
            new CharacterApiContext(1, 100, "synthetic-token", "800000001", "prod_official_asia"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(ds, Does.Match(@"^\d+,[A-Za-z0-9]{6},[0-9a-f]{32}$"));
            Assert.That(clientType, Is.EqualTo("5"));
            Assert.That(appVersion, Is.EqualTo("1.5.0"));
        }
    }

    [Test]
    public async Task GameRoleApiService_SendsReversedCookieOrder()
    {
        const string profilesJson = """
            {"retcode":0,"message":"OK","data":{"list":[
              {"game_biz":"hk4e_global","region":"os_asia","game_uid":"800000001","nickname":"Player","level":60}
            ]}}
            """;
        var cache = new Mock<ICacheService>();
        var handler = new FakeHttpMessageHandler();
        string? cookie = null;
        handler.Enqueue(request =>
        {
            cookie = request.Headers.GetValues("Cookie").Single();
            return JsonResponse(profilesJson);
        });

        var service = new GameRoleApiService(handler.ToHttpClientFactory(), cache.Object,
            new Mock<ILogger<GameRoleApiService>>().Object);
        var result = await service.GetAsync(
            new GameRoleApiContext(1, 100, "synthetic-token", Game.Genshin, "os_asia"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(cookie, Is.EqualTo("ltoken_v2=synthetic-token; ltuid_v2=100"));
        }
    }

    [Test]
    public async Task CodeRedeemApiService_Utf16JsonCharset_IsParsed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"retcode":0,"message":"OK","data":null}""",
                Encoding.Unicode, "application/json")
        });

        var service = new CodeRedeemApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<CodeRedeemApiService>>().Object);
        var result = await service.GetAsync(
            new CodeRedeemApiContext(1, 2, "synthetic-token", "800000001", "os_asia", Game.Genshin, "TESTCODE"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True,
                "Cdkey success must not require a data payload and must honour the declared charset");
            Assert.That(result.Data.Outcome, Is.EqualTo(RedemptionOutcome.Redeemed));
        }
    }

    [Test]
    public async Task ZzzCharacterApiService_ColdCacheDetail_HttpFailureCarriesRequestUri()
    {
        const string requestUri =
            "https://sg-public-api.hoyolab.com/event/game_record_zzz/api/zzz/avatar/info?id_list[]=1011&server=prod_gf_jp&role_id=600000001&need_wiki=true";
        var handler = new FakeHttpMessageHandler();
        string? cookie = null;
        string? language = null;
        handler.Enqueue(request =>
        {
            cookie = request.Headers.GetValues("Cookie").Single();
            language = request.Headers.TryGetValues("X-Rpc-Language", out var values) ? values.Single() : null;
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        });

        var service = new ZzzCharacterApiService(new Mock<ICacheService>().Object,
            handler.ToHttpClientFactory(), new Mock<ILogger<ZzzCharacterApiService>>().Object);
        var result = await service.GetCharacterDetailAsync(
            new CharacterApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp", 1011));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
            Assert.That(result.RequestUri, Is.EqualTo(requestUri),
                "HTTP failures now report the endpoint that was attempted");
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.LastRequest.RequestUri!.ToString(), Is.EqualTo(requestUri));
            Assert.That(language, Is.EqualTo("en-us"));
            Assert.That(cookie, Is.EqualTo("ltuid_v2=100; ltoken_v2=synthetic-token"));
        }
    }

    [Test]
    public async Task ZzzCharacterApiService_ColdCacheDetail_MalformedCredentialCarriesRequestUri()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new ZzzCharacterApiService(new Mock<ICacheService>().Object,
            handler.ToHttpClientFactory(), new Mock<ILogger<ZzzCharacterApiService>>().Object);

        var result = await service.GetCharacterDetailAsync(
            new CharacterApiContext(1, 100, "bad token", "600000001", "prod_gf_jp", 1011));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task ZzzCharacterApiService_ColdCacheDetail_AuthAndParseFailuresCarryRequestUri()
    {
        var authHandler = new FakeHttpMessageHandler();
        authHandler.EnqueueJson("""{"retcode":10001,"message":"auth"}""");
        var authService = new ZzzCharacterApiService(new Mock<ICacheService>().Object,
            authHandler.ToHttpClientFactory(), new Mock<ILogger<ZzzCharacterApiService>>().Object);
        var authResult = await authService.GetCharacterDetailAsync(
            new CharacterApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp", 1011));

        var parseHandler = new FakeHttpMessageHandler();
        parseHandler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });
        var parseService = new ZzzCharacterApiService(new Mock<ICacheService>().Object,
            parseHandler.ToHttpClientFactory(), new Mock<ILogger<ZzzCharacterApiService>>().Object);
        var parseResult = await parseService.GetCharacterDetailAsync(
            new CharacterApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp", 1011));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(authResult.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(authResult.RequestUri, Is.Not.Null);
            Assert.That(parseResult.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(parseResult.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
            Assert.That(parseResult.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task ZzzTowerApiService_AuthRetcodeWithoutData_ReturnsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":10001,"message":"auth"}""");

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task ZzzTowerApiService_StringNumbers_AreProjectedWithSharedOptions()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""
            {"retcode":"0","message":"OK","data":{"climbing_tower_s3":{
              "layer_info":{"climbing_tower_layer":"7","total_score":"123","medal_icon":"m.png"},
              "mvp_info":{"floor_mvp_num":"1","rank_percent":"2"},
              "display_avatar_rank_list":[{"avatar_id":"1","icon":"i.png","name":"N","rarity":"S"}]}}}
            """);

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
            Assert.That(result.Data.Payload!.LayerInfo.ClimbingTowerLayer, Is.EqualTo(7));
            Assert.That(result.Data.Payload.LayerInfo.TotalScore, Is.EqualTo(123));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task ZzzTowerApiService_InvalidPayload_ReturnsExternalServerError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"climbing_tower_s3":{"layer_info":{}}}}""");

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(
                "An unknown error occurred when accessing HoYoLAB API. Please try again later"));
        }
    }

    [Test]
    public async Task ZzzTowerApiService_UnknownRetcode_DoesNotEchoUpstreamText()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$"""{"retcode":12345,"message":"{{UpstreamCanary}}","data":null}""");

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
        }
    }

    [TestCase("\"invalid\"")]
    [TestCase("\"999999999999999999999999999999999999999\"")]
    public async Task ZzzTowerApiService_MalformedRetcode_ReturnsControlledExternalServerError(string retcodeJson)
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            "{\"retcode\":" + retcodeJson + ",\"message\":\"" + UpstreamCanary + "\",\"data\":{}}");

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(
                "An unknown error occurred when accessing HoYoLAB API. Please try again later"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task ZzzTowerApiService_StringAuthRetcode_ReturnsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$"""{"retcode":"10001","message":"{{UpstreamCanary}}","data":null}""");

        var service = new ZzzTowerApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzTowerApiService>>().Object);
        var result = await service.GetAsync(
            new BaseHoYoApiContext(1, 100, "synthetic-token", "600000001", "prod_gf_jp"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_WarmCache_ReturnsCacheWithoutHttp()
    {
        var cached = new ZzzCharacterEntryPageList
        {
            List = [new ZzzCharacterEntryPageData { EntryPageId = "1", Name = "Cached" }],
            Total = 1
        };
        var cache = CreateCache();
        cache.Setup(c => c.GetAsync<ZzzCharacterEntryPageList>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var handler = new FakeHttpMessageHandler();
        var service = CreateEntryPageService(cache, handler);

        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.List.Single().Name, Is.EqualTo("Cached"));
            Assert.That(result.RequestUri, Is.Null);
            Assert.That(handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_PaginatesInsideSingleScopeAndCachesAggregate()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        var pageNumbers = new List<int>();
        var tokens = new List<CancellationToken>();

        for (var page = 1; page <= 3; page++)
        {
            var pageNumber = page;
            handler.Enqueue(request =>
            {
                using var document = JsonDocument.Parse(
                    request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                pageNumbers.Add(document.RootElement.GetProperty("page_num").GetInt32());
                return JsonResponse(EntryPageJson(pageNumber, total: 120));
            });
        }

        var service = CreateEntryPageService(cache, handler);

        cache.Setup(c => c.SetAsync(It.IsAny<ICacheEntry<ZzzCharacterEntryPageList>>(),
                It.IsAny<CancellationToken>()))
            .Callback<ICacheEntry<ZzzCharacterEntryPageList>, CancellationToken>((_, token) => tokens.Add(token))
            .Returns(Task.CompletedTask);

        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(pageNumbers, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(handler.RequestCount, Is.EqualTo(3));
            Assert.That(result.Data!.List, Has.Count.EqualTo(3));
            Assert.That(result.Data.Total, Is.EqualTo(120));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(tokens, Has.Count.EqualTo(1));
            Assert.That(tokens[0].CanBeCanceled, Is.True,
                "The aggregate cache write must use the linked request scope token");
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_FirstPageNullList_ReturnsControlledFailureWithoutCacheWrite()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"list":null,"total":51}}""");
        handler.Enqueue(() => JsonResponse(EntryPageJson(2, total: 51)));

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while retrieving ZZZ entry page list"));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.EqualTo(1), "A null list must stop pagination");
            cache.Verify(c => c.SetAsync(It.IsAny<ICacheEntry<ZzzCharacterEntryPageList>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_LaterPageNullList_ReturnsControlledFailureWithoutCacheWrite()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => JsonResponse(EntryPageJson(1, total: 120)));
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"list":null,"total":120}}""");

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while retrieving ZZZ entry page list"));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.EqualTo(2));
            cache.Verify(c => c.SetAsync(It.IsAny<ICacheEntry<ZzzCharacterEntryPageList>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_EmptyFirstPageList_IsAllowedAndCached()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"list":[],"total":0}}""");

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.List, Is.Empty);
            Assert.That(result.Data.Total, Is.Zero);
            Assert.That(result.RequestUri, Is.Not.Null);
            cache.Verify(c => c.SetAsync(It.IsAny<ICacheEntry<ZzzCharacterEntryPageList>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_UnknownRetcode_LogsSanitizedScalarMetadataOnly()
    {
        const string sensitiveCanary = "SYNTHETIC-ENTRY-PAGE-CANARY";
        const string safeNameCanary = "SYNTHETIC-SAFE-ENTRY-PAGE-NAME";
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson($$$"""
            {"retcode":12345,"message":"{{{sensitiveCanary}}}","data":{"list":[
              {"entry_page_id":"1","name":"{{{safeNameCanary}}}"}
            ],"total":51}}
            """);

        var logger = new CapturingLogger<ZzzCharacterEntryPageApiService>();
        var service = new ZzzCharacterEntryPageApiService(handler.ToHttpClientFactory(), logger, cache.Object);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        var metadata = logger.States.SelectMany(entry => entry).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo("An error occurred while retrieving ZZZ entry page list"));
            Assert.That(result.ErrorMessage, Does.Not.Contain(sensitiveCanary));
            Assert.That(logger.States, Is.Not.Empty);
            Assert.That(logger.Entries.Any(entry => entry.Contains(sensitiveCanary, StringComparison.Ordinal)),
                Is.False, "Sensitive response text must not appear in formatted log output");
            Assert.That(metadata.Any(pair => pair.Key == "Retcode"), Is.True);
            Assert.That(metadata.Where(pair => pair.Key != "{OriginalFormat}").Select(pair => pair.Value)
                    .All(value => value is null or string or ValueType), Is.True,
                "Failure diagnostics must capture scalar metadata and pre-rendered sanitized strings only");
            Assert.That(metadata.Where(pair => pair.Key == "{OriginalFormat}").Select(pair => pair.Value as string)
                    .All(format => format is not null && !format.Contains("{@", StringComparison.Ordinal)), Is.True,
                "Structured log templates must not destructure response payloads");
            Assert.That(metadata.Any(pair => pair.Key == "SanitizedResponseBody" &&
                                             pair.Value is string body &&
                                             body.Contains(safeNameCanary, StringComparison.Ordinal)), Is.True,
                "Ordinary wiki fields in a known model remain visible after sanitization");
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_HttpFailure_ReturnsControlledMessageWithoutUpstreamText()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = UpstreamCanary,
            Content = new StringContent($$"""{"retcode":0,"message":"{{UpstreamCanary}}","data":null}""",
                Encoding.UTF8, "application/json")
        });

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_MalformedPageJson_ReturnsExternalServerError()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("{not-json");

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Is.EqualTo(DefaultRequestFailure));
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_LaterPageFailure_StopsPagination()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => JsonResponse(EntryPageJson(1, total: 120)));
        handler.EnqueueJson($$"""{"retcode":12345,"message":"{{UpstreamCanary}}"}""");

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Does.Not.Contain(UpstreamCanary));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
            cache.Verify(c => c.SetAsync(It.IsAny<ICacheEntry<ZzzCharacterEntryPageList>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_PreCancelledCaller_ReturnsCancelledWithoutHttp()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ZzzCharacterEntryPageList>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, token) =>
                Task.FromCanceled<ZzzCharacterEntryPageList?>(token));

        var handler = new FakeHttpMessageHandler();
        var service = CreateEntryPageService(cache, handler);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1), cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Cancelled));
            Assert.That(result.RequestUri, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task ZzzCharacterEntryPageApiService_Timeout_ReturnsTimeout()
    {
        var cache = CreateCache();
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => throw new TaskCanceledException("simulated timeout"));

        var service = CreateEntryPageService(cache, handler);
        var result = await service.GetAsync(new ZzzCharacterEntryPageApiContext(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Timeout));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    private static Mock<ICacheService> CreateCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ZzzCharacterEntryPageList>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ZzzCharacterEntryPageList?)null);
        return cache;
    }

    private static ZzzCharacterEntryPageApiService CreateEntryPageService(Mock<ICacheService> cache,
        FakeHttpMessageHandler handler)
    {
        return new ZzzCharacterEntryPageApiService(handler.ToHttpClientFactory(),
            new Mock<ILogger<ZzzCharacterEntryPageApiService>>().Object, cache.Object);
    }

    private static string EntryPageJson(int pageNumber, int total)
    {
        return $$$"""
            {"retcode":0,"message":"OK","data":{"list":[
              {"entry_page_id":"{{{pageNumber}}}","name":"Page {{{pageNumber}}}"}
            ],"total":{{{total}}}}}
            """;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public List<IReadOnlyList<KeyValuePair<string, object?>>> States { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
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

    private sealed class TrackingContent : ByteArrayContent
    {
        public TrackingContent(string body) : base(Encoding.UTF8.GetBytes(body))
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TestPayload
    {
        [JsonPropertyName("value")] public int Value { get; init; }
    }

    private sealed class TestPostPayload
    {
        [JsonPropertyName("role_id")] public string RoleId { get; init; } = string.Empty;

        [JsonPropertyName("server")] public string Server { get; init; } = string.Empty;

        [JsonPropertyName("sort_type")] public int SortType { get; init; }
    }
}
