#region

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Telemetry;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// Credentials for one HoYoLAB request. The ltoken is validated by <see cref="HoYoLabApi"/> before a Cookie header
/// exists, so malformed values can never reach <see cref="System.Net.Http.Headers.HttpHeaders.Add(string, string)"/>.
/// </summary>
internal readonly record struct HoYoLabCredentials(ulong UserId, ulong LtUid, string LToken);

/// <summary>
/// HoYoLAB retcodes that select the authentication failure branch of the common response envelope.
/// </summary>
internal static class HoYoLabRetcodes
{
    /// <summary>Authentication retcode used by the game_record endpoints.</summary>
    public const int GameRecordAuth = 10001;

    /// <summary>Authentication retcode used by the account, cdkey and check-in endpoints.</summary>
    public const int AccountAuth = -100;
}

/// <summary>
/// Failure copy for one endpoint. All values are controlled strings owned by the bot; upstream response text is
/// never interpolated into a user-visible message. Endpoints without a justified exception use
/// <see cref="Default"/> by leaving <see cref="HoYoLabRequest.Failures"/> unset.
/// </summary>
internal sealed record HoYoLabFailureMessages
{
    private const string GenericFailure =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    /// <summary>Shared failure copy for endpoints that do not need endpoint-specific messages.</summary>
    public static HoYoLabFailureMessages Default { get; } = new();

    public string Unauthorized { get; init; } = "Invalid HoYoLAB UID or Cookies. Please authenticate again.";

    /// <summary>Returned when a successful HTTP response contains no parseable JSON envelope.</summary>
    public string Parse { get; init; } = GenericFailure;

    /// <summary>Returned for every non-success HTTP status code.</summary>
    public string NonSuccessStatus { get; init; } = GenericFailure;
}

/// <summary>
/// The wire side of one HoYoLAB request: endpoint, method, body, headers and credentials mode.
/// </summary>
internal sealed record HoYoLabRequest
{
    public required string RequestUri { get; init; }

    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>Serialized with <see cref="JsonSerializer"/> defaults and sent as UTF-8 JSON.</summary>
    public object? Body { get; init; }

    /// <summary>Headers other than Cookie, applied in order after the Cookie header.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    public bool RequiresCredentials { get; init; } = true;

    /// <summary>Cookie header template; {0} is the ltuid and {1} is the ltoken.</summary>
    public string CookieFormat { get; init; } = "ltuid_v2={0}; ltoken_v2={1}";

    /// <summary>Failure copy; defaults to <see cref="HoYoLabFailureMessages.Default"/>.</summary>
    public HoYoLabFailureMessages Failures { get; init; } = HoYoLabFailureMessages.Default;

    /// <summary>
    /// Optional expected-miss policy for this endpoint. Only wiki content lookups currently opt in: an HTTP 404 is
    /// logged as a warning and the matching HTTP span is normalized by the Application trace processor. The result
    /// mapping is unchanged, so callers still receive <see cref="StatusCode.ExternalServerError"/>.
    /// </summary>
    public ExpectedDependencyKind ExpectedDependency { get; init; } = ExpectedDependencyKind.None;
}

/// <summary>
/// Retcode and data expectations for one endpoint. The service keeps endpoint-specific post-processing (caching,
/// pagination, domain gates) outside the executor.
/// </summary>
internal sealed record HoYoLabEnvelopePolicy<T> where T : class
{
    /// <summary>
    /// Authentication retcode. Defaults to the game-record code; set to <see langword="null"/> for anonymous
    /// endpoints that have no authentication scheme.
    /// </summary>
    public int? AuthRetcode { get; init; } = HoYoLabRetcodes.GameRecordAuth;

    /// <summary>Optional non-null data predicate; defaults to a null check.</summary>
    public Func<T, bool>? HasData { get; init; }

    public required string UnknownRetcode { get; init; }

    /// <summary>Empty-data message; defaults to <see cref="UnknownRetcode"/>.</summary>
    public string? EmptyData { get; init; }
}

/// <summary>
/// Linked cancellation scope for one API call. The caller token is used for cancellation reporting while
/// <see cref="Token"/> bounds cache and HTTP work to <see cref="IApiService.MaxTimeoutSeconds"/>.
/// </summary>
internal sealed class HoYoLabRequestScope : IDisposable
{
    private readonly CancellationTokenSource m_TimeoutSource;

    public HoYoLabRequestScope(CancellationToken cancellationToken)
    {
        m_TimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        m_TimeoutSource.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));
        Token = m_TimeoutSource.Token;
    }

    public CancellationToken Token { get; }

    public void Dispose()
    {
        m_TimeoutSource.Dispose();
    }
}

/// <summary>
/// Shared request pipeline for HoYoLAB endpoints. Owns cookie construction, credential validation, the HTTP request,
/// status handling, charset-aware JSON parsing and the common retcode/data mapping. Exceptions other than parsing
/// failures are intentionally not caught here: each service keeps its existing catch block so request building,
/// caching and post-processing failures keep their original <see cref="StatusCode.BotError"/> mapping and
/// cancellation semantics.
/// </summary>
internal static class HoYoLabApi
{
    private const string HttpClientName = "Default";

    /// <summary>
    /// Serializer settings shared by every envelope and JSON-node endpoint. Web defaults match case-insensitive
    /// property names and accept numbers encoded as strings; property attributes and converters on the DTOs still
    /// apply.
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sends a request and parses the common <see cref="ApiResponse{T}"/> envelope. Escape hatch for endpoints with
    /// domain-specific retcode outcomes (cdkey, check-in).
    /// </summary>
    public static Task<Result<ApiResponse<T>>> SendEnvelopeAsync<T>(HoYoLabRequest request,
        HoYoLabCredentials credentials, IHttpClientFactory httpClientFactory, ILogger logger,
        CancellationToken cancellationToken, int? authRetcode = null) where T : class
    {
        return SendAsync(request, credentials, httpClientFactory, logger,
            ParseEnvelope<T>,
            envelope => envelope.Retcode,
            envelopeDiagnostics: true,
            authRetcode,
            cancellationToken);
    }

    /// <summary>
    /// Sends an anonymous or credentialed request and parses the response as a <see cref="JsonNode"/> without applying
    /// retcode mapping. Escape hatch for endpoints whose payload is projected directly from the raw node.
    /// </summary>
    public static Task<Result<JsonNode>> SendJsonAsync(HoYoLabRequest request, HoYoLabCredentials credentials,
        IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
    {
        return SendAsync(request, credentials, httpClientFactory, logger,
            ParseJsonNode,
            node => TryReadRetcode(node, out var retcode) ? retcode : -1,
            envelopeDiagnostics: false,
            authRetcode: null,
            cancellationToken);
    }

    /// <summary>
    /// Runs the common envelope pipeline: transport, authentication retcode, unknown retcode and data validation.
    /// </summary>
    public static async Task<Result<T>> ExecuteAsync<T>(HoYoLabRequest request, HoYoLabEnvelopePolicy<T> policy,
        HoYoLabCredentials credentials, IHttpClientFactory httpClientFactory, ILogger logger,
        CancellationToken cancellationToken) where T : class
    {
        var envelope = await SendEnvelopeAsync<T>(request, credentials, httpClientFactory, logger,
            cancellationToken, policy.AuthRetcode);
        if (!envelope.IsSuccess)
            return Result<T>.Failure(envelope.StatusCode, envelope.ErrorMessage, envelope.RequestUri);

        return MapEnvelope(envelope.Data, request, policy, logger, credentials.UserId);
    }

    /// <summary>
    /// Runs the common envelope pipeline and delegates endpoint-specific post-processing (caching, DTO projection,
    /// domain gates) to <paramref name="map"/>.
    /// </summary>
    public static async Task<Result<TResult>> ExecuteAsync<TEnvelope, TResult>(HoYoLabRequest request,
        HoYoLabEnvelopePolicy<TEnvelope> policy, HoYoLabCredentials credentials,
        IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken,
        Func<TEnvelope, Task<Result<TResult>>> map) where TEnvelope : class
    {
        var mapped = await ExecuteAsync(request, policy, credentials, httpClientFactory, logger, cancellationToken);
        if (!mapped.IsSuccess)
            return Result<TResult>.Failure(mapped.StatusCode, mapped.ErrorMessage, mapped.RequestUri);

        return await map(mapped.Data);
    }

    /// <summary>
    /// Builds a controlled failure for a parsed envelope whose required endpoint structure is malformed. The log is
    /// bounded and redacted exactly like a parse failure, and the message is owned by the calling service.
    /// </summary>
    internal static Result<GameApiResponse<T>> InvalidEndpointData<T>(string? requestUri, ILogger logger,
        ulong userId, string errorMessage) where T : class
    {
        logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
            GameApiLog.ContextId(userId), GameApiLog.SafeQueryParameters(requestUri), null, null,
            GameApiLog.OmittedMarker);
        return Result<GameApiResponse<T>>.Failure(StatusCode.ExternalServerError, errorMessage, requestUri);
    }

    /// <summary>
    /// Reads a numeric retcode from a raw JSON node, accepting values encoded as strings.
    /// </summary>
    internal static bool TryReadRetcode(JsonNode? node, out int retcode)
    {
        retcode = -1;

        if (node is not JsonObject root || !root.TryGetPropertyValue("retcode", out var value) ||
            value is not JsonValue jsonValue)
            return false;

        if (jsonValue.TryGetValue<int>(out var numericRetcode))
        {
            retcode = numericRetcode;
            return true;
        }

        if (jsonValue.TryGetValue<string>(out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRetcode))
        {
            retcode = parsedRetcode;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Projects a raw JSON node with the shared serializer settings, reporting malformed payloads instead of
    /// throwing.
    /// </summary>
    internal static bool TryDeserializeNode<T>(JsonNode? node, [NotNullWhen(true)] out T? value) where T : class
    {
        value = null;

        if (node is null)
            return false;

        try
        {
            value = JsonSerializer.Deserialize<T>(node, JsonOptions);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static Result<T> MapEnvelope<T>(ApiResponse<T> envelope, HoYoLabRequest request,
        HoYoLabEnvelopePolicy<T> policy, ILogger logger, ulong userId) where T : class
    {
        if (policy.AuthRetcode is { } authRetcode && envelope.Retcode == authRetcode)
        {
            logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(userId));
            return Result<T>.Failure(StatusCode.Unauthorized, request.Failures.Unauthorized, request.RequestUri);
        }

        if (envelope.Retcode != 0)
        {
            logger.LogError(LogMessages.UnknownRetcode, envelope.Retcode, GameApiLog.ContextId(userId),
                GameApiLog.Endpoint(request.RequestUri), GameApiLog.SafeQueryParameters(request.RequestUri),
                GameApiLog.RenderBody(envelope));
            return Result<T>.Failure(StatusCode.ExternalServerError, policy.UnknownRetcode, request.RequestUri);
        }

        if (!HasData(envelope.Data, policy.HasData))
        {
            logger.LogError(LogMessages.EmptyResponseData, GameApiLog.Endpoint(request.RequestUri), GameApiLog.ContextId(userId),
                GameApiLog.SafeQueryParameters(request.RequestUri), GameApiLog.RenderBody(envelope));
            return Result<T>.Failure(StatusCode.ExternalServerError, policy.EmptyData ?? policy.UnknownRetcode,
                request.RequestUri);
        }

        return Result<T>.Success(envelope.Data!, requestUri: request.RequestUri);
    }

    private static async Task<Result<T>> SendAsync<T>(HoYoLabRequest request, HoYoLabCredentials credentials,
        IHttpClientFactory httpClientFactory, ILogger logger,
        Func<string, T?> parse, Func<T, int> retcodeSelector, bool envelopeDiagnostics, int? authRetcode,
        CancellationToken cancellationToken) where T : class
    {
        using var expectedDependency = request.ExpectedDependency == ExpectedDependencyKind.None
            ? null
            : ExpectedDependency.Begin(request.ExpectedDependency);

        if (request.RequiresCredentials && !LTokenValidator.IsValidLToken(credentials.LToken))
        {
            // Reject malformed credential characters/lengths before building the Cookie header. HttpHeaders.Add
            // would throw a FormatException embedding the credential value, which the service catch block would
            // write to retained file/OTLP logs. This sanitized warning never includes the credential itself.
            logger.LogWarning("Rejected HoYoLAB request with malformed credential format for User {UserId} at {Endpoint}",
                GameApiLog.ContextId(credentials.UserId), GameApiLog.Endpoint(request.RequestUri));
            return Result<T>.Failure(StatusCode.Unauthorized, request.Failures.Unauthorized, request.RequestUri);
        }

        var endpoint = GameApiLog.Endpoint(request.RequestUri);
        var safeQueryParameters = GameApiLog.SafeQueryParameters(request.RequestUri);

        logger.LogInformation(LogMessages.PreparingRequest, endpoint, safeQueryParameters);

        cancellationToken.ThrowIfCancellationRequested();

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var message = CreateRequestMessage(request, credentials);
        // ResponseHeadersRead keeps the body unbuffered so authentication responses are never read and diagnostic
        // reads stay inside the bounded reader instead of being materialized by the transport.
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var contentType = GameApiLog.Text(response.Content.Headers.ContentType?.MediaType, 128);

        if (!response.IsSuccessStatusCode)
        {
            var capture = IsBodyMetadataOnly(response.StatusCode)
                ? GameApiLogBodyCapture.NotRead(contentType, response.Content.Headers.ContentLength)
                : await GameApiLogBodyReader.ReadAsync(response.Content, cancellationToken);

            var diagnosticBody = RenderDiagnosticBody(capture, envelopeDiagnostics, parse, retcodeSelector,
                authRetcode);
            var expectedContentMiss =
                request.ExpectedDependency == ExpectedDependencyKind.WikiContentLookup &&
                response.StatusCode == HttpStatusCode.NotFound;

            if (expectedContentMiss)
            {
                logger.LogWarning(LogMessages.NonSuccessStatusCode, response.StatusCode, endpoint,
                    safeQueryParameters, capture.ContentType, capture.ContentLength, diagnosticBody);
            }
            else
            {
                logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, endpoint,
                    safeQueryParameters, capture.ContentType, capture.ContentLength, diagnosticBody);
            }

            return Result<T>.Failure(StatusCode.ExternalServerError, request.Failures.NonSuccessStatus,
                request.RequestUri);
        }

        string? body = null;
        T? parsed;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            parsed = parse(body);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogError(LogMessages.FailedToParseResponse, endpoint, GameApiLog.ContextId(credentials.UserId), safeQueryParameters,
                contentType, response.Content.Headers.ContentLength,
                RenderBodyDiagnostics(body, envelopeDiagnostics, parse, retcodeSelector, authRetcode));
            return Result<T>.Failure(StatusCode.ExternalServerError, request.Failures.Parse, request.RequestUri);
        }

        if (parsed == null)
        {
            logger.LogError(LogMessages.FailedToParseResponse, endpoint, GameApiLog.ContextId(credentials.UserId), safeQueryParameters,
                contentType, response.Content.Headers.ContentLength,
                RenderBodyDiagnostics(body, envelopeDiagnostics, parse, retcodeSelector, authRetcode));
            return Result<T>.Failure(StatusCode.ExternalServerError, request.Failures.Parse, request.RequestUri);
        }

        logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, endpoint,
            retcodeSelector(parsed), GameApiLog.ContextId(credentials.UserId), safeQueryParameters);

        return Result<T>.Success(parsed, requestUri: request.RequestUri);
    }

    private static bool IsBodyMetadataOnly(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or
            HttpStatusCode.ProxyAuthenticationRequired;
    }

    private static string RenderDiagnosticBody<T>(GameApiLogBodyCapture capture, bool envelopeDiagnostics,
        Func<string, T?> parse, Func<T, int> retcodeSelector, int? authRetcode) where T : class
    {
        if (capture.UnavailableReason is not null)
            return GameApiLog.Unavailable(capture.UnavailableReason);

        return capture.Body is null
            ? GameApiLog.NotReadMarker
            : RenderBodyDiagnostics(capture.Body, envelopeDiagnostics, parse, retcodeSelector, authRetcode);
    }

    private static string RenderBodyDiagnostics<T>(string? body, bool envelopeDiagnostics, Func<string, T?> parse,
        Func<T, int> retcodeSelector, int? authRetcode) where T : class
    {
        if (body is null)
            return GameApiLog.NotReadMarker;

        if (!envelopeDiagnostics)
            return GameApiLog.OmittedMarker;

        if (body.Length > GameApiLogBodyReader.MaxBodyBytes)
            return GameApiLog.Unavailable(GameApiLogBodyReader.SizeLimitReason);

        try
        {
            var typedEnvelope = parse(body);
            if (typedEnvelope is not null)
            {
                return IsAuthRetcode(retcodeSelector(typedEnvelope), authRetcode)
                    ? GameApiLog.AuthBodySuppressedMarker
                    : GameApiLog.RenderBody(typedEnvelope);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // The typed shape did not match; fall back to the shared envelope below.
        }

        var envelope = TryParseEnvelopeDiagnostics(body);
        if (envelope is null)
            return GameApiLog.OmittedMarker;

        return IsAuthRetcode(envelope.Retcode, authRetcode)
            ? GameApiLog.AuthBodySuppressedMarker
            : GameApiLog.RenderBody(envelope);
    }

    private static bool IsAuthRetcode(int retcode, int? authRetcode)
    {
        return authRetcode is { } expected && retcode == expected;
    }

    private static ApiResponse<object>? TryParseEnvelopeDiagnostics(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiResponse<object>>(body, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateRequestMessage(HoYoLabRequest request, HoYoLabCredentials credentials)
    {
        var message = new HttpRequestMessage(request.Method, request.RequestUri);

        try
        {
            if (request.Body is not null)
                message.Content = new StringContent(JsonSerializer.Serialize(request.Body), Encoding.UTF8,
                    "application/json");

            if (request.RequiresCredentials)
                message.Headers.Add("Cookie",
                    string.Format(request.CookieFormat, credentials.LtUid, credentials.LToken));

            foreach (var header in request.Headers)
                message.Headers.Add(header.Key, header.Value);

            return message;
        }
        catch
        {
            message.Dispose();
            throw;
        }
    }

    private static ApiResponse<T>? ParseEnvelope<T>(string body) where T : class
    {
        return JsonSerializer.Deserialize<ApiResponse<T>>(body, JsonOptions);
    }

    private static JsonNode? ParseJsonNode(string body)
    {
        return JsonNode.Parse(body);
    }

    private static bool HasData<T>(T? data, Func<T, bool>? predicate) where T : class
    {
        return data is not null && (predicate?.Invoke(data) ?? true);
    }
}
