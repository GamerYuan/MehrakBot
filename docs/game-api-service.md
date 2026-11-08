# Adding a HoYoLAB API service

`Mehrak.GameApi` shares one request pipeline (`Shared/HoYoLabApi.cs`) across every HoYoLAB endpoint: game-record,
character, account, code-redeem, check-in, wiki and the ZZZ character entry-page list. A new service describes the
endpoint, its headers and its result mapping; it does not re-implement the timeout, credential check, Cookie header,
HTTP send, status handling, JSON parsing, error copy or cancellation reporting.

## Minimal service

```csharp
internal sealed class ExampleApiService : IApiService<ExampleData, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record/genshin/api/example";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ExampleApiService> m_Logger;

    public ExampleApiService(IHttpClientFactory httpClientFactory, ILogger<ExampleApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ExampleData>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ExampleData>.Failure(StatusCode.BadParameter, "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                    }
                },
                new HoYoLabEnvelopePolicy<ExampleData>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<ExampleData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<ExampleData>.Failure(StatusCode.BotError,
                "An error occurred while fetching example data", requestUri);
        }
    }
}
```

Register the service in `Common/Mehrak.GameApi/GameApiServiceCollectionExtension.cs`:

```csharp
services.AddSingleton<IApiService<ExampleData, BaseHoYoApiContext>, ExampleApiService>();
```

The constructor shape `(IHttpClientFactory, ILogger<T>)` matches the existing registrations. Services that need the
cache take `ICacheService` in the constructor instead, as the character services do.

## Standard evaluation order

Every service evaluates the same sequence, in the same order:

1. Local parameter checks (`context.GameUid`, `context.Region`, ids, game mapping).
2. Cache short-circuit, when the endpoint caches.
3. HTTP send through the shared pipeline.
4. Non-success HTTP status.
5. Envelope/JSON parsing.
6. Authentication retcode.
7. Known domain retcodes and outcomes (cdkey, check-in).
8. Any other non-zero retcode.
9. Data validation.
10. Endpoint/domain mapping (DTO projection, caching, pagination, unlock gates).

Do not add endpoint-specific `data`-before-retcode branches. An authentication retcode without data is always
`Unauthorized`; a non-zero retcode without data is always `ExternalServerError`. Domain gates such as the
Imaginarium Theater unlock/empty-data checks run only after the retcode and envelope data have been accepted.

## Where endpoint behavior goes

| Concern | Where |
| --- | --- |
| URL, query string, method, JSON body, `X-Rpc-*` headers, `DS` | `HoYoLabRequest` |
| Cookie pair order / trailing `;` | `HoYoLabRequest.CookieFormat` (`{0}` = ltuid, `{1}` = ltoken) |
| Anonymous endpoint | `HoYoLabRequest.RequiresCredentials = false` |
| Authentication retcode (`10001` game-record, `-100` account/cdkey/check-in) | `HoYoLabEnvelopePolicy<T>.AuthRetcode` (`HoYoLabRetcodes`); `null` for anonymous endpoints with no auth scheme |
| Non-null/empty data definition | `HoYoLabEnvelopePolicy<T>.HasData` |
| Error copy | `HoYoLabFailureMessages` and `HoYoLabEnvelopePolicy<T>` |
| Caching, pagination, DTO mapping, domain gates | The service, outside the executor |

`HoYoLabRetcodes.GameRecordAuth` (`10001`) is the default policy value because most game-record endpoints use it.
Change it to `HoYoLabRetcodes.AccountAuth` (`-100`) for the account/cdkey/check-in endpoints, and to `null` for
endpoints that are anonymous or have no authentication branch. Never map `10001` on an endpoint that does not use it.

## Error copy

All user-visible failure copy is a controlled string owned by the bot:

* `Unauthorized`: the credential message returned by the auth retcode and by malformed-credential rejection.
* `Parse`: returned when the response body is missing, malformed or cannot be projected into the DTO.
* `NonSuccessStatus`: returned for every non-success HTTP status, including 401, 403 and 5xx.
* `UnknownRetcode` / `EmptyData`: returned for non-zero retcodes and missing data.

Never interpolate `ApiResponse.Message`, `HttpResponseMessage.ReasonPhrase`, exception text or any other upstream
value into these strings. A numerical retcode and sanitized ordinary response fields may be logged for diagnosis, but
they are never echoed as user copy.

## JSON parsing and serializer options

There is exactly one parsing path for typed envelopes and `JsonNode` endpoints. The body is read with the declared
`Content-Type` charset (including UTF-16) and deserialized with `HoYoLabApi.JsonOptions`, which uses
`JsonSerializerDefaults.Web`: case-insensitive property names and numbers encoded as strings are accepted, while
`[JsonPropertyName]`, converters and `required` members on the DTOs still apply.

Raw-node endpoints project with the same options (`HoYoLabApi.TryDeserializeNode<T>`), so string-encoded numbers
behave consistently there too. `HoYoLabApi.TryReadRetcode` reads a retcode from a node, including a retcode encoded
as a string.

Malformed JSON and invalid payload shapes become a controlled `ExternalServerError` with the endpoint's `Parse`
message. They are never reported as `BotError`, and the exception text is never shown to users.

## Escape hatches

Use these when the common envelope mapping does not fit; they still share cookie construction, credential
validation, timeout, send, status handling, charset-aware parsing and inbound logging.

* `HoYoLabApi.SendEnvelopeAsync<T>` returns the parsed `ApiResponse<T>` and leaves retcode mapping to the service.
  Used by `CodeRedeemApiService` and `DailyCheckInApiService` (known non-zero success outcomes) and by
  `GameRoleApiService` (profile-specific final mapping).
* `HoYoLabApi.SendJsonAsync` returns a `JsonNode`. Used by `WikiApiService` (anonymous, no auth scheme) and
  `ZzzTowerApiService` (payload projected from nested nodes).

`ZzzCharacterEntryPageApiService` uses the typed executor for every page, so it inherits the same status, parse,
cancellation and error-copy behavior as the other endpoints. `ImageUpdaterService` is an image downloader rather
than an API service and keeps its own HTTP handling.

## Cancellation and disposal

Create exactly one `HoYoLabRequestScope` per `GetAsync` call. The scope links the caller token with the 30-second
`IApiService.MaxTimeoutSeconds` budget, and `scope.Token` must be passed to every cache read, HTTP send, parse,
pagination step and cache write in that call. Do not create a new timeout budget per page.

Report cancellation with the caller's `cancellationToken` and the endpoint URI:

```csharp
catch (OperationCanceledException)
{
    return Result<T>.FromCancellation(cancellationToken, requestUri);
}
```

A caller-cancelled token reports `Cancelled`; an expired 30-second scope reports `Timeout`. Local parameter checks
may run before the scope, but a pre-cancelled caller must not send HTTP after validation: the shared pipeline checks
the token before creating the request.

The executor owns and disposes the `HttpRequestMessage` (including when body or header setup throws) and the
`HttpResponseMessage`. It does not dispose the `HttpClient` supplied by `IHttpClientFactory`.

## RequestUri

Return the endpoint URI on every result once it has been determined: HTTP attempts and failures, parse and retcode
failures, cancellation and internal errors. Cache hits, and failures that happen before the URI is built (invalid
parameters, unsupported game contexts), keep `RequestUri` null. Never build a query string with invalid parameters
just to populate the result.

## Logging

### Where redaction happens

All request and response sanitization lives in `Mehrak.GameApi/Shared` and runs **before** any value reaches
`ILogger`. The three hosts that use these services—Application (`AddGameApiServices`), Dashboard and Bot (direct
`GameRoleApiService` / `HylPostApiService` registration)—therefore get the same behavior without host, DI, Serilog or
ServiceDefaults changes. Service constructors and public interfaces are unchanged.

Do not enable `EnableRedaction`, `AddExtendedHttpClientLogging(LogBody = true)` or a global payload logger as an
alternative: the former only redacts explicitly classified tags, and the latter forwards raw bodies that this
pipeline deliberately never exposes. The sanitizer is provider-independent, so console, file and OTLP sinks all
receive already-sanitized scalars.

`Mehrak.GameApi.csproj` references `Microsoft.Extensions.Compliance.Redaction` 10.7.0 (its net10 dependency aligns with
the repository's 10.0.9 extensions packages). `ErasingRedactor.Instance` is used directly inside the helper, so no
`AddRedaction()` registration is needed in any host.

### Request metadata

* The logged endpoint is `scheme://host/path` only. User info, the entire query string and the fragment are removed;
  an unparseable URI becomes `<invalid-uri>`, never the original string.
* Every query value is erased, including repeated, percent-encoded and mixed-case parameters.
* The only retained query value is `SafeQueryParameters`, and only for `entry_page_id` when exactly one occurrence
  consists of 1–12 ASCII digits. Encoded, duplicated, non-numeric or overlong values produce `<none>`. Values from
  `WikiApiContext.EntryPage` are never trusted directly.
* Cookie, `Set-Cookie`, `Authorization`, `Proxy-Authorization`, `DS` signatures and every other header value are never
  logged. The pipeline logs no request body.
* Bot user ids and HoYoLAB account ids are erased with the same `ErasingRedactor` helper before logging, including
  validation, success, cache, auth, shared, local-catch and exception events. The field stays visible as
  `<redacted>`; ordinary game ids/levels and the validated `entry_page_id` remain visible.
* `Result.RequestUri` and the wire URI keep the full unredacted value; redaction applies only to log fields.

### Response failure diagnostics

The shared pipeline and the endpoint-local failure branches log a bounded, sanitized body when one is available:

* Known response models are rendered with their ordinary public fields by default. `[GameApiSensitive]` (a project
  classification attribute derived from `DataClassificationAttribute`) marks members that are erased with
  `Microsoft.Extensions.Compliance.Redaction.ErasingRedactor` before rendering. Currently classified members are
  `ApiResponse<T>.Message`, `GameRole.GameProfile.GameUid`/`Nickname`, `GameRecord.GameData.GameRoleId`/`Nickname` and
  `HylPostDetail.Uid`/`Subject`/`Content`/`StructuredContent`. Ordinary character, wiki, region, level and record
  fields stay visible.
* Opaque or unmodeled shapes are omitted with a stable marker: `object`, `JsonNode`, `JsonElement`, dictionaries with
  dynamic keys, streams, byte arrays, exceptions, delegates and framework types. A malformed, HTML or unsupported body
  is reported as `<omitted>`; no raw excerpt is ever logged.
* Rendering is bounded to 4096 characters, depth 6, 10 array items, 200 nodes and 256 characters per string. The
  diagnostic body read is capped at 64 KiB and uses the declared charset (including UTF-16). A body that exceeds the
  cap is reported as `<unavailable:SizeLimit>`, not truncated from its raw text. The shared send uses
  `HttpCompletionOption.ResponseHeadersRead`, so the transport does not buffer a response body up front. Non-success
  HTTP response bodies, other than 401, 403 and 407, are read by the bounded diagnostic reader, which requests at most
  the remaining cap plus one sentinel byte. Successful HTTP response bodies are read in full for normal parsing
  under the linked request timeout, not the diagnostic size cap.
* 401, 403 and 407 responses log status, endpoint, safe query and content metadata only (`<not-read>`); the body is
  not read. Authentication-retcode envelopes are suppressed to a fixed `<auth>` marker before either the typed or the
  fallback diagnostic renderer runs, using the endpoint's configured `AuthRetcode` (no blanket suppression for
  anonymous endpoints whose policy is `null`). Successful responses never log a body.
* A diagnostic read/parse failure must not change the result: a known HTTP failure stays `ExternalServerError` and a
  known failure never becomes `BotError` because diagnostics failed.

### Exception metadata

Endpoint catch blocks call `GameApiLog.LogException`, which logs only the sanitized endpoint, the erased bot user id
marker (`<redacted>`) and the exception **type** name. Raw exception messages, stack traces, inner exceptions and
`Data` are never forwarded: an HttpClient/`FormatException` message can contain the URI, query values or credential
fragments.

Synthetic failure event:

```
API returned non-zero retcode 1234 for User <redacted> at endpoint: https://example.invalid/api with safe query
entry_page_id=123. Sanitized response body: {"data":{"level":60},"message":"<redacted>","retcode":1234}
```

### Legacy endpoint logs

`LogMessages.UnknownRetcode` no longer destructures the envelope; it carries scalars plus a pre-rendered sanitized body.
The former endpoint-local raw-object logs in cdkey, check-in, profile, wiki and tower branches now use the same helper.
Code redemption success/failure logs no longer include the cdkey or game UID, and profile/check-in logs no longer
include the HoYoLAB ltuid. Image-updater transport behavior is unchanged, but its URL log fields are sanitized through
the same endpoint helper.

Redaction here is log-only. HTTP client trace tags and telemetry remain a separate concern; do not claim universal
telemetry redaction based on this pipeline.
